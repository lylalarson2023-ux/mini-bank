using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using MBANK_ETUDIANT.Services;
using MBANK_ETUDIANT.Components;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/mbank-.log", rollingInterval: RollingInterval.Day,
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

var loginSecret = Guid.NewGuid().ToString("N");
builder.Configuration["LoginSecret"] = loginSecret;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<BankDbContext>(options =>
    options.UseSqlite("Data Source=MbankData.db"));

// --- STRIPE ---
var stripeSecret = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
var stripePublishable = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");
var stripeWebhook = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
if (!string.IsNullOrEmpty(stripeSecret)) builder.Configuration["Stripe:SecretKey"] = stripeSecret;
if (!string.IsNullOrEmpty(stripePublishable)) builder.Configuration["Stripe:PublishableKey"] = stripePublishable;
if (!string.IsNullOrEmpty(stripeWebhook)) builder.Configuration["Stripe:WebhookSecret"] = stripeWebhook;
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<StripeService>();

// --- SERVICES MÉTIER SPÉCIALISÉS ---
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MBANK_ETUDIANT.Services.AccountService>();
builder.Services.AddScoped<SavingsService>();
builder.Services.AddScoped<CreditService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<MBANK_ETUDIANT.Services.FileService>();
builder.Services.AddScoped<BankService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NotificationHistoryService>();

// --- AUTHENTIFICATION AVEC COOKIE ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "MBANK_AUTH";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
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
            FOREIGN KEY (UserId) REFERENCES UserProfiles(Id)
        )"); } catch { }

    if (!db.UserProfiles.Any(u => u.IsAdmin))
    {
        db.UserProfiles.Add(new UserProfile
        {
            Nom = "Admin",
            Prenom = "System",
            Email = "admin@mbank.ma",
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            IsAdmin = true,
            Solde = 1_000_000m,
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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// --- ENDPOINT DE CONNEXION (pose le cookie auth) ---
// Protégé par un HMAC : impossible d'usurper un userId sans connaître la clé secrète
static string SignToken(string payload, string secret)
{
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{payload}:{secret}")));
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
app.MapGet("/api/stripe/success", async (HttpContext ctx, StripeService stripe, string session_id, int user_id) =>
{
    await stripe.ConfirmerDepotAsync(session_id);
    return Results.Redirect("/depot?paid=ok");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
