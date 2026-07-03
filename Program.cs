using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using ADN_pay.Services;
using ADN_pay.Components;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;
using Stripe;
using Polly;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "ADN_pay");

// En service Windows, le répertoire courant est System32 → on ancre sur le dossier
// de l'exe (publish/). En dev, on garde le dossier de travail (racine du projet),
// pour ne pas déplacer la base/les logs vers bin/.
var baseDir = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()
    ? AppContext.BaseDirectory
    : Directory.GetCurrentDirectory();

var logDir = Path.Combine(baseDir, "logs");
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(logDir, "adnpay-.log"), rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Error)
    .CreateLogger();

builder.Host.UseSerilog();

// Credentials admin lus depuis les variables d'environnement (sécurité)
var envUser = Environment.GetEnvironmentVariable("ADMIN_API_USERNAME");
var envPass = Environment.GetEnvironmentVariable("ADMIN_API_PASSWORD");
if (!string.IsNullOrEmpty(envUser)) builder.Configuration["AdminApi:Username"] = envUser;
if (!string.IsNullOrEmpty(envPass)) builder.Configuration["AdminApi:Password"] = envPass;

var loginSecret = builder.Configuration["LoginSecret"] ?? Guid.NewGuid().ToString("N");
builder.Configuration["LoginSecret"] = loginSecret;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    });

builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    options.MaxBufferedUnacknowledgedRenderBatches = 20;
});

var dbPath = Path.Combine(baseDir, "AdnPayData.db");
// Factory (et non AddDbContext scoped) : en Blazor Server le scope DI vit toute la
// durée du circuit, donc un DbContext scoped est partagé par tous les composants →
// « A second operation was started on this context instance… » au moindre double-clic.
// Chaque service crée/dispose son propre contexte par opération via IDbContextFactory.
builder.Services.AddDbContextFactory<BankDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// --- RATE LIMITING ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("Strict", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// --- STRIPE ---
var stripeSecret = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
var stripePublishable = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");
var stripeWebhook = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
if (!string.IsNullOrEmpty(stripeSecret)) builder.Configuration["Stripe:SecretKey"] = stripeSecret;
if (!string.IsNullOrEmpty(stripePublishable)) builder.Configuration["Stripe:PublishableKey"] = stripePublishable;
if (!string.IsNullOrEmpty(stripeWebhook)) builder.Configuration["Stripe:WebhookSecret"] = stripeWebhook;
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<StripeService>();

// --- VIREMENT BANCAIRE (coordonnées affichées au client ; dépôt validé par l'admin) ---
var virRib = Environment.GetEnvironmentVariable("VIREMENT_RIB");
var virBanque = Environment.GetEnvironmentVariable("VIREMENT_BANQUE");
var virTitulaire = Environment.GetEnvironmentVariable("VIREMENT_TITULAIRE");
if (!string.IsNullOrEmpty(virRib)) builder.Configuration["Virement:Rib"] = virRib;
if (!string.IsNullOrEmpty(virBanque)) builder.Configuration["Virement:Banque"] = virBanque;
if (!string.IsNullOrEmpty(virTitulaire)) builder.Configuration["Virement:Titulaire"] = virTitulaire;

// --- MOBILE MONEY MANUEL (phase pilote : envoi direct sur le numéro du fondateur,
//     référence en motif, validation admin — plafonds gérés par BankTransferService) ---
var mmNumero = Environment.GetEnvironmentVariable("MOBILEMONEY_NUMERO");
var mmOperateur = Environment.GetEnvironmentVariable("MOBILEMONEY_OPERATEUR");
var mmTitulaire = Environment.GetEnvironmentVariable("MOBILEMONEY_TITULAIRE");
var mmXafParDh = Environment.GetEnvironmentVariable("MOBILEMONEY_XAF_PAR_DH");
if (!string.IsNullOrEmpty(mmNumero)) builder.Configuration["MobileMoney:Numero"] = mmNumero;
if (!string.IsNullOrEmpty(mmOperateur)) builder.Configuration["MobileMoney:Operateur"] = mmOperateur;
if (!string.IsNullOrEmpty(mmTitulaire)) builder.Configuration["MobileMoney:Titulaire"] = mmTitulaire;
if (!string.IsNullOrEmpty(mmXafParDh)) builder.Configuration["MobileMoney:XafParDh"] = mmXafParDh;

// --- ALERTING (ADR-007) ---
builder.Services.AddHttpClient<IAlertingService, AlertingService>();

// --- E-MAIL (Brevo en prod, log en dev) ---
var brevoKey = Environment.GetEnvironmentVariable("BREVO_API_KEY");
if (!string.IsNullOrEmpty(brevoKey)) builder.Configuration["Brevo:ApiKey"] = brevoKey;
var brevoFrom = Environment.GetEnvironmentVariable("BREVO_SENDER_EMAIL");
if (!string.IsNullOrEmpty(brevoFrom)) builder.Configuration["Brevo:SenderEmail"] = brevoFrom;

// Brevo uniquement si une clé est dispo ET (hors dev OU forçage explicite). Sinon : log dev.
var useBrevo = !string.IsNullOrEmpty(builder.Configuration["Brevo:ApiKey"])
    && (!builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Email:ForceRealInDev"));
if (useBrevo)
    builder.Services.AddHttpClient<IEmailSender, BrevoEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();

// --- SERVICES MÉTIER SPÉCIALISÉS ---
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ADN_pay.Services.AccountService>();
builder.Services.AddScoped<SavingsService>();
builder.Services.AddScoped<CreditService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ADN_pay.Services.FileService>();
builder.Services.AddScoped<BankService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NotificationHistoryService>();
builder.Services.AddScoped<TwoFactorService>();
// Crédit idempotent des dépôts externes (Stripe, Flutterwave, virement) — sans UserContext,
// utilisable depuis les webhooks/callbacks serveur→serveur.
builder.Services.AddScoped<ExternalDepositService>();
builder.Services.AddScoped<BankTransferService>();

// --- AUTHENTIFICATION AVEC COOKIE ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ADN_PAY_AUTH";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Prod (HTTPS via Cloudflare) : cookie Secure strict.
        // Dev : SameAsRequest, pour que le cookie soit accepté en HTTP simple sur le
        // réseau local (test LAN sur http://<IP>:5163, où Secure ferait jeter le cookie).
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});

var app = builder.Build();

// --- MIGRATIONS AUTO + SEED ADMIN ---
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();

    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN PremiumValidatedAt datetime"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN PremiumRejectedAt datetime"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS CreditRequests (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId INTEGER NOT NULL,
            Montant TEXT NOT NULL,
            DureeMois INTEGER NOT NULL,
            Categorie TEXT NOT NULL DEFAULT '',
            Statut TEXT NOT NULL DEFAULT 'EN_ATTENTE',
            DateDemande TEXT NOT NULL,
            TauxAnnuel TEXT NOT NULL DEFAULT '0',
            MotifRejet TEXT,
            FOREIGN KEY (UserId) REFERENCES UserProfiles(Id)
        )"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN KycRejetMotif TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN TwoFactorEnabled INTEGER NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN TwoFactorSecret TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN Role TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN CompteCloture INTEGER NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN DateCloture datetime"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN TwoFactorRecoveryCodes TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN PendingEmail TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN EmailChangeCodeHash TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN EmailChangeCodeExpiry datetime"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN Bloque INTEGER NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN AdnEmail TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("UPDATE UserProfiles SET Role = '' WHERE Role IS NULL"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE SavingsPockets ADD COLUMN TuteurVisible INTEGER NOT NULL DEFAULT 0"); } catch { }
    // Idempotence des dépôts externes (Stripe/Flutterwave/virement) : référence unique.
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Transactions ADD COLUMN ReferenceExterne TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Transactions_ReferenceExterne ON Transactions(ReferenceExterne) WHERE ReferenceExterne IS NOT NULL"); } catch { }
    // Demandes de dépôt par virement bancaire (l'admin les crée aussi).
    try { db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS BankTransferRequests (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId INTEGER NOT NULL,
            MontantCentimes INTEGER NOT NULL,
            Reference TEXT NOT NULL,
            Statut TEXT NOT NULL DEFAULT 'EN_ATTENTE',
            MotifRejet TEXT,
            DateCreation TEXT NOT NULL,
            DateTraitement TEXT,
            TraitePar TEXT,
            FOREIGN KEY (UserId) REFERENCES UserProfiles(Id)
        )"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_BankTransferRequests_Reference ON BankTransferRequests(Reference)"); } catch { }
    // Canal de dépôt manuel (virement / mobile money) + montant converti affiché au client.
    try { db.Database.ExecuteSqlRaw("ALTER TABLE BankTransferRequests ADD COLUMN Canal TEXT NOT NULL DEFAULT 'VIREMENT'"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE BankTransferRequests ADD COLUMN MontantConverti INTEGER"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE BankTransferRequests ADD COLUMN DeviseConvertie TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE CreditRequests ADD COLUMN TauxAnnuel TEXT NOT NULL DEFAULT '0'"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE CreditRequests ADD COLUMN MotifRejet TEXT"); } catch { }

    // Migration UserLogins.UserId → nullable (permet d'enregistrer les tentatives sur email inconnu)
    try { db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS UserLogins_new (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId INTEGER,
            Email TEXT NOT NULL,
            Date TEXT NOT NULL,
            IpAddress TEXT NOT NULL,
            UserAgent TEXT NOT NULL,
            Success INTEGER NOT NULL,
            FailureReason TEXT
        )"); } catch { }
    try { db.Database.ExecuteSqlRaw("INSERT OR IGNORE INTO UserLogins_new SELECT * FROM UserLogins"); } catch { }
    try { db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS UserLogins_old"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserLogins RENAME TO UserLogins_old"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserLogins_new RENAME TO UserLogins"); } catch { }

    if (!db.UserProfiles.Any(u => u.IsAdmin))
    {
        var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL")
            ?? app.Configuration["Admin:Email"]
            ?? "admin@adnpay.ma";
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? app.Configuration["Admin:Password"];
        if (string.IsNullOrEmpty(adminPassword))
        {
            adminPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace("+", "!").Replace("/", "@").Replace("=", "#");
            Log.Warning("=== ADMIN PASSWORD GENERE (configurer ADMIN_PASSWORD) : {Pwd} ===", adminPassword);
        }
        db.UserProfiles.Add(new UserProfile
        {
            Nom = "Admin",
            Prenom = "System",
            Email = adminEmail,
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            IsAdmin = true,
            Solde = 100_000_000L,
            Statut = UserStatus.VIP,
            CguAcceptees = true
        });
        db.SaveChanges();
        Log.Information("Compte admin créé : {Email}", adminEmail);
    }
    else if (string.Equals(Environment.GetEnvironmentVariable("ADMIN_PASSWORD_RESET"), "true", StringComparison.OrdinalIgnoreCase))
    {
        // Réinitialisation VOLONTAIRE et ponctuelle (ADMIN_PASSWORD_RESET=true).
        // Sans ce drapeau, on ne touche jamais au mot de passe admin au démarrage,
        // pour ne pas écraser un changement fait par l'admin via l'UI.
        var configuredPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? app.Configuration["Admin:Password"];
        if (!string.IsNullOrEmpty(configuredPassword))
        {
            var admin = db.UserProfiles.First(u => u.IsAdmin);
            admin.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(configuredPassword);
            db.SaveChanges();
            Log.Warning("Mot de passe admin réinitialisé depuis la configuration (ADMIN_PASSWORD_RESET).");
        }
    }

    // Migration des mots de passe existants vers BCrypt
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.MigreMotsDePasseEnClair();
}

app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseHsts();

app.UseForwardedHeaders();
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// --- ENDPOINT DE CONNEXION (pose le cookie auth) ---
static string SignToken(string payload, string secret)
{
    return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));
}

app.MapGet("/api/auth/login-handler", async (HttpContext ctx, IDbContextFactory<BankDbContext> dbFactory, int userId, long exp, string sig) =>
{
    if (sig != SignToken($"{userId}.{exp}", loginSecret))
        return Results.Redirect("/login?error=invalid");
    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
        return Results.Redirect("/login?error=session");

    await using var db = await dbFactory.CreateDbContextAsync();
    var user = await db.UserProfiles.FindAsync(userId);
    if (user == null) return Results.Redirect("/login?error=invalid");

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Email),
        new(ClaimTypes.GivenName, user.Prenom),
        new(ClaimTypes.Surname, user.Nom),
        new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User"),
        new("SecurityStamp", user.SecurityStamp),
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
});

// --- ENDPOINT DE DÉCONNEXION (supprime le cookie) ---
app.MapGet("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// --- EXPORT CSV DES UTILISATEURS (admin uniquement) ---
app.MapGet("/api/admin/users.csv", async (IDbContextFactory<BankDbContext> dbFactory) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    static string Esc(string? v)
    {
        v ??= "";
        return (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }

    var users = await db.UserProfiles.OrderBy(u => u.Id).ToListAsync();
    var sb = new StringBuilder();
    sb.Append('﻿'); // BOM UTF-8 pour Excel (accents)
    sb.AppendLine("Id,Nom,Prenom,Email,Telephone,Statut,Solde_DH,Role,DateInscription,TuteurEmail");
    foreach (var u in users)
    {
        sb.AppendLine(string.Join(",",
            u.Id,
            Esc(u.Nom),
            Esc(u.Prenom),
            Esc(u.Email),
            Esc(u.Telephone),
            Esc(u.Statut.ToString()),
            (u.Solde / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Esc(u.IsAdmin ? "Admin" : "User"),
            u.DateInscription.ToString("yyyy-MM-dd"),
            Esc(u.TuteurEmail)));
    }

    var fileName = $"utilisateurs_adnpay_{DateTime.UtcNow:yyyyMMdd}.csv";
    return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", fileName);
}).RequireAuthorization("AdminOnly");

// --- STRIPE SUCCESS (retour après paiement réussi) ---
// Le statut est re-vérifié auprès de l'API Stripe et le crédit est idempotent
// (référence de session unique) : recharger cette URL ne crédite pas deux fois.
app.MapGet("/api/stripe/success", async (HttpContext ctx, StripeService stripe, string session_id) =>
{
    await stripe.ConfirmerDepotAsync(session_id);
    return Results.Redirect("/depot?paid=ok");
});

// --- STRIPE WEBHOOK (filet de sécurité serveur→serveur) ---
// Crédite même si l'utilisateur ferme son navigateur avant la redirection de succès.
// Signature vérifiée avec STRIPE_WEBHOOK_SECRET ; 404 si non configuré.
app.MapPost("/api/stripe/webhook", async (HttpContext ctx,
    Microsoft.Extensions.Options.IOptions<StripeOptions> stripeOpts,
    ExternalDepositService deposits, ILogger<Program> logger) =>
{
    var secret = stripeOpts.Value.WebhookSecret;
    if (string.IsNullOrEmpty(secret)) return Results.NotFound();

    var json = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    Stripe.Event stripeEvent;
    try
    {
        stripeEvent = Stripe.EventUtility.ConstructEvent(json, ctx.Request.Headers["Stripe-Signature"], secret);
    }
    catch (Stripe.StripeException ex)
    {
        logger.LogWarning("Webhook Stripe rejeté (signature invalide) : {Message}", ex.Message);
        return Results.BadRequest();
    }

    if (stripeEvent.Type == "checkout.session.completed"
        && stripeEvent.Data.Object is Stripe.Checkout.Session session)
    {
        var ok = await StripeService.CrediterDepuisSessionAsync(session, deposits);
        logger.LogInformation("Webhook Stripe checkout.session.completed — session={SessionId}, crédité={Ok}",
            session.Id, ok);
    }

    return Results.Ok();
});

// --- UPLOAD FICHIER (hors circuit Blazor — évite le bug de formulaire qui se vide) ---
app.MapPost("/api/upload", async (HttpRequest request, ADN_pay.Services.FileService fileService) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Requête invalide" });
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "Aucun fichier" });
    if (file.Length > 2 * 1024 * 1024)
        return Results.BadRequest(new { error = "Fichier trop volumineux (max 2 Mo)" });
    var url = await fileService.EnregistrerFichierSurDisque(file);
    if (string.IsNullOrEmpty(url))
        return Results.BadRequest(new { error = "Fichier invalide" });
    return Results.Ok(new { url });
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
