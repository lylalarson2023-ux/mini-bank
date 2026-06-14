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

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/adnpay-.log", rollingInterval: RollingInterval.Day,
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
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<BankDbContext>(options =>
    options.UseSqlite("Data Source=AdnPayData.db"));

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

// --- PAWAPAY (Mobile Money) ---
var pawaPayToken = Environment.GetEnvironmentVariable("PAWAPAY_API_TOKEN");
if (!string.IsNullOrEmpty(pawaPayToken)) builder.Configuration["PawaPay:ApiToken"] = pawaPayToken;
builder.Services.Configure<PawaPayOptions>(builder.Configuration.GetSection("PawaPay"));
builder.Services.AddHttpClient("PawaPay")
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
builder.Services.AddScoped<IPawaPayService, PawaPayService>();

// --- ALERTING (ADR-007) ---
builder.Services.AddHttpClient<IAlertingService, AlertingService>();

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

// --- AUTHENTIFICATION AVEC COOKIE ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ADN_PAY_AUTH";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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
    var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
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
    try { db.Database.ExecuteSqlRaw("UPDATE UserProfiles SET Role = '' WHERE Role IS NULL"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE SavingsPockets ADD COLUMN TuteurVisible INTEGER NOT NULL DEFAULT 0"); } catch { }
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
        db.UserProfiles.Add(new UserProfile
        {
            Nom = "Admin",
            Prenom = "System",
            Email = "admin@adnpay.ma",
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            IsAdmin = true,
            Solde = 100_000_000L,
            Statut = UserStatus.VIP,
            CguAcceptees = true
        });
        db.SaveChanges();
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

app.MapGet("/api/auth/login-handler", async (HttpContext ctx, BankDbContext db, int userId, string sig) =>
{
    if (sig != SignToken(userId.ToString(), loginSecret))
        return Results.Redirect("/login?error=invalid");

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

// --- STRIPE SUCCESS (retour après paiement réussi) ---
app.MapGet("/api/stripe/success", async (HttpContext ctx, StripeService stripe, string session_id) =>
{
    await stripe.ConfirmerDepotAsync(session_id);
    return Results.Redirect("/depot?paid=ok");
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

// --- PAWAPAY CALLBACK (appel serveur→serveur, pas d'auth) ---
app.MapPost("/api/pawapay/callback", async (HttpContext ctx, IPawaPayService pawaPay, ILogger<Program> logger) =>
{
    try
    {
        var dto = await ctx.Request.ReadFromJsonAsync<PawaPayCallbackDto>();
        if (dto == null || string.IsNullOrEmpty(dto.DepositId))
            return Results.BadRequest(new { error = "Invalid payload" });

        await pawaPay.HandleCallbackAsync(dto);
        logger.LogInformation("PawaPay callback processed — depositId={DepositId}, status={Status}", dto.DepositId, dto.Status);
        return Results.Ok(new { received = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "PawaPay callback processing failed");
        return Results.Ok(new { received = true }); // always ACK to PawaPay
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
