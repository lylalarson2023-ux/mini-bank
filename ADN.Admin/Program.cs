using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ADN_pay.Admin.Components;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

// Secret de signature pour la passation login → cookie (Blazor ne peut pas poser
// le cookie depuis le circuit : on valide puis on redirige vers un endpoint HTTP signé).
var loginSecret = builder.Configuration["LoginSecret"]
    ?? Environment.GetEnvironmentVariable("ADMIN_LOGIN_SECRET")
    ?? Guid.NewGuid().ToString("N");
builder.Configuration["LoginSecret"] = loginSecret;

// --- BASE DE DONNÉES (partagée avec l'app principale) ---
var dbPath = Environment.GetEnvironmentVariable("ADN_DB_PATH")
    ?? builder.Configuration["Db:Path"]
    ?? Path.Combine(AppContext.BaseDirectory, "AdnPayData.db");
builder.Services.AddDbContextFactory<BankDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

// --- BLAZOR SERVER ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- AUTH COOKIE (mur : rôle Admin exigé) ---
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
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// --- SERVICES MÉTIER (réutilisés de la couche Application) ---
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<NotificationHistoryService>();
builder.Services.AddScoped<AdminService>();

var app = builder.Build();

// --- DÉMARRAGE : schéma + (option) garantie d'un compte admin pour le dev ---
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    db.Database.EnsureCreated();

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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// --- PASSATION LOGIN → COOKIE (endpoint HTTP signé) ---
static string Sign(string payload, string secret) =>
    Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

app.MapGet("/auth/signin", async (HttpContext ctx, IDbContextFactory<BankDbContext> dbFactory, int userId, string sig) =>
{
    if (sig != Sign(userId.ToString(), loginSecret))
        return Results.Redirect("/login?error=invalid");

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
    Log.Information("Connexion admin : {Email}", user.Email);
    return Results.Redirect("/");
});

app.MapGet("/auth/signout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
