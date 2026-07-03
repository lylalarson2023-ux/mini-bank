using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using ADN_pay.Admin.Components;
using ADN_pay.Api.Endpoints;
using ADN_pay.Api.Middleware;
using ADN_pay.Api.Tokens;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- LOGGING ---
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "adnadmin-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Error)
    .CreateLogger();
builder.Host.UseSerilog();

// Secret de signature pour la passation login admin → cookie (Blazor ne peut pas poser
// le cookie depuis le circuit : on valide puis on redirige vers un endpoint HTTP signé).
var loginSecret = builder.Configuration["LoginSecret"]
    ?? Environment.GetEnvironmentVariable("ADMIN_LOGIN_SECRET")
    ?? Guid.NewGuid().ToString("N");
builder.Configuration["LoginSecret"] = loginSecret;

// --- SECRET JWT (API REST) ---
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? builder.Configuration["Jwt:Secret"];
if (string.IsNullOrEmpty(jwtSecret))
{
    jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    Log.Warning("JWT_SECRET non configuré — clé aléatoire générée (les tokens ne survivront pas au redémarrage). Configurez JWT_SECRET.");
}
builder.Configuration["Jwt:Secret"] = jwtSecret;

// --- GARDE-FOU : jamais les secrets jetables de dev en production ---
if (builder.Environment.IsProduction())
{
    var devSecrets = new[]
    {
        "adminverifysecret123456",
        "dev-merge-jwt-secret-ZGV2LW1lcmdlLTMyYnl0ZXMtbWluaW11bS1sZW5ndGgtb2s=",
    };
    if (devSecrets.Contains(loginSecret) || devSecrets.Contains(jwtSecret) || jwtSecret.StartsWith("dev-")
        || Environment.GetEnvironmentVariable("ADMIN_PASSWORD") == "AdminVerif1!")
        throw new InvalidOperationException(
            "Secrets de DEV détectés en Production (ADMIN_LOGIN_SECRET / JWT_SECRET / ADMIN_PASSWORD). " +
            "Générez de vrais secrets aléatoires avant le déploiement.");
}

// --- BASE DE DONNÉES (partagée avec l'app principale) ---
var dbPath = Environment.GetEnvironmentVariable("ADN_DB_PATH")
    ?? builder.Configuration["Db:Path"]
    ?? Path.Combine(AppContext.BaseDirectory, "AdnPayData.db");
builder.Services.AddDbContextFactory<BankDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

// --- BLAZOR SERVER ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- AUTH : cookie (admin, schéma par défaut) + JWT bearer (API REST) ---
var tokenSvc = new JwtTokenService(builder.Configuration);
builder.Services.AddSingleton(tokenSvc);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ADN_ADMIN_AUTH";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.SlidingExpiration = true;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme,
        opt => opt.TokenValidationParameters = tokenSvc.GetValidationParameters());

builder.Services.AddAuthorization(options =>
{
    // Admin Blazor : rôle Admin via le cookie (schéma par défaut).
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    // API REST : authentifié via le schéma JWT bearer UNIQUEMENT (un cookie admin
    // ne donne pas accès à l'API, et un token API ne donne pas accès à l'admin).
    options.AddPolicy("ApiBearer", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
    // API REST réservée aux admins (rôle Admin porté par le JWT).
    options.AddPolicy("ApiAdmin", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireRole("Admin"));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// --- CORS (clients mobiles de l'API) ---
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// --- RATE LIMITING (anti brute-force sur l'authentification, par IP) ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});
// Anti-brute-force du login admin Blazor (hors de portée du middleware HTTP).
builder.Services.AddSingleton<ADN_pay.Admin.Services.LoginThrottle>();
// Vérification TOTP/codes de secours à la connexion admin (couche Application).
builder.Services.AddScoped<TwoFactorService>();

// --- SERVICES MÉTIER (couche Application, partagés admin + API) ---
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<NotificationHistoryService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<SavingsService>();
builder.Services.AddScoped<CreditService>();
builder.Services.AddScoped<ADN_pay.Admin.Services.ToastService>();
builder.Services.AddScoped<ExternalDepositService>();
builder.Services.AddScoped<BankTransferService>();
builder.Services.AddScoped<UserContextMiddleware>();
// E-mail : Brevo en prod (clé dispo + hors dev ou forçage), sinon log dev.
var brevoKeyAdmin = Environment.GetEnvironmentVariable("BREVO_API_KEY");
if (!string.IsNullOrEmpty(brevoKeyAdmin)) builder.Configuration["Brevo:ApiKey"] = brevoKeyAdmin;
var useBrevoAdmin = !string.IsNullOrEmpty(builder.Configuration["Brevo:ApiKey"])
    && (!builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Email:ForceRealInDev"));
if (useBrevoAdmin)
    builder.Services.AddHttpClient<IEmailSender, ADN_pay.Api.Services.BrevoEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, ADN_pay.Api.Services.LogEmailSender>();

// --- SWAGGER (API REST) ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new() { Title = "ADN_pay API", Version = "v1" });
    opt.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Description = "JWT Bearer : entrez 'Bearer <token>'",
        Name = "Authorization",
        Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    opt.AddSecurityRequirement(doc => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        { new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", doc), new List<string>() }
    });
});

var app = builder.Build();

// --- DÉMARRAGE : schéma + table RefreshTokens (API) + (option) admin dev ---
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    db.Database.EnsureCreated();

    try
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS RefreshTokens (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                TokenHash TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                Revoked INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                DeviceInfo TEXT
            )");
    }
    catch { }

    // Colonne de blocage admin (idempotent — l'app web la crée aussi).
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN Bloque INTEGER NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN AdnEmail TEXT NOT NULL DEFAULT ''"); } catch { }
    // Idempotence des dépôts externes (Stripe/Flutterwave/virement) — l'app web la crée aussi.
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Transactions ADD COLUMN ReferenceExterne TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Transactions_ReferenceExterne ON Transactions(ReferenceExterne) WHERE ReferenceExterne IS NOT NULL"); } catch { }
    // Demandes de dépôt par virement bancaire (l'app web la crée aussi).
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
    // Canal de dépôt manuel (virement / mobile money) — l'app web la crée aussi.
    try { db.Database.ExecuteSqlRaw("ALTER TABLE BankTransferRequests ADD COLUMN Canal TEXT NOT NULL DEFAULT 'VIREMENT'"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE BankTransferRequests ADD COLUMN MontantConverti INTEGER"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE BankTransferRequests ADD COLUMN DeviseConvertie TEXT"); } catch { }

    // Pratique de dev : si ADMIN_EMAIL + ADMIN_PASSWORD sont fournis, on garantit
    // que ce compte existe en tant qu'admin (création ou MAJ). Sinon on ne touche à rien.
    var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
    var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var email = adminEmail.Trim().ToLower();
        var existing = db.UserProfiles.FirstOrDefault(u => u.Email == email);
        if (existing == null)
        {
            db.UserProfiles.Add(new UserProfile
            {
                Nom = "Admin",
                Prenom = "System",
                Email = email,
                MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                IsAdmin = true,
                Statut = UserStatus.VIP,
                CguAcceptees = true
            });
            Log.Information("Compte admin créé : {Email}", email);
        }
        else
        {
            existing.IsAdmin = true;
            existing.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
            Log.Information("Compte admin mis à jour : {Email}", email);
        }
        await db.SaveChangesAsync();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Swagger : dev uniquement (ou forçage explicite Swagger:Enabled) — ne pas
// cartographier l'API publiquement en production.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(opt => opt.SwaggerEndpoint("/swagger/v1/swagger.json", "ADN_pay API v1"));
}

// UserContext pour l'API : peuplé depuis le JWT, uniquement sur /api/v1 (l'admin Blazor
// peuple le sien depuis le cookie dans MainLayout).
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/v1"),
    branch => branch.UseMiddleware<UserContextMiddleware>());

// --- PASSATION LOGIN ADMIN → COOKIE (endpoint HTTP signé) ---
static string Sign(string payload, string secret) =>
    Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

app.MapGet("/auth/signin", async (HttpContext ctx, IDbContextFactory<BankDbContext> dbFactory, int userId, long exp, string sig) =>
{
    if (sig != Sign($"{userId}.{exp}", loginSecret))
        return Results.Redirect("/login?error=invalid");
    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
        return Results.Redirect("/login?error=expired");

    await using var db = await dbFactory.CreateDbContextAsync();
    var user = await db.UserProfiles.FindAsync(userId);
    if (user == null || !user.IsAdmin)
        return Results.Redirect("/login?error=denied");

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Email),
        new(ClaimTypes.GivenName, user.Prenom),
        new(ClaimTypes.Surname, user.Nom),
        new(ClaimTypes.Role, "Admin"),
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    db.UserLogins.Add(new UserLogin
    {
        UserId = user.Id, Email = user.Email, Date = DateTime.UtcNow, Success = true,
        IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? "admin", UserAgent = "admin"
    });
    await db.SaveChangesAsync();
    Log.Information("Connexion admin : {Email}", user.Email);
    return Results.Redirect("/");
}).RequireRateLimiting("auth");

app.MapGet("/auth/signout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// --- EXPORT CSV DES UTILISATEURS (admin cookie uniquement) ---
app.MapGet("/api/admin/users.csv", async (IDbContextFactory<BankDbContext> dbFactory) =>
{
    static string Esc(string? v)
    {
        v ??= "";
        return (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
            ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    await using var db = await dbFactory.CreateDbContextAsync();
    var users = await db.UserProfiles.OrderBy(u => u.Id).ToListAsync();
    var sb = new StringBuilder();
    sb.Append('﻿'); // BOM UTF-8 (accents dans Excel)
    sb.AppendLine("Id,Nom,Prenom,Email,Telephone,Statut,Solde_DH,Role,DateInscription,TuteurEmail");
    foreach (var u in users)
    {
        sb.AppendLine(string.Join(",",
            u.Id, Esc(u.Nom), Esc(u.Prenom), Esc(u.Email), Esc(u.Telephone),
            Esc(u.Statut.ToString()),
            (u.Solde / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Esc(u.IsAdmin ? "Admin" : "User"),
            u.DateInscription.ToString("yyyy-MM-dd"), Esc(u.TuteurEmail)));
    }
    return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8",
        $"utilisateurs_adnpay_{DateTime.UtcNow:yyyyMMdd}.csv");
}).RequireAuthorization("AdminOnly");

// --- API REST (JWT bearer) ---
AuthEndpoints.Map(app);
AccountEndpoints.Map(app);
SavingsEndpoints.Map(app);
CreditEndpoints.Map(app);
AdminEndpoints.Map(app);
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow })).WithTags("Health");

// --- ADMIN BLAZOR ---
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
