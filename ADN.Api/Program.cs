using ADN_pay.Api.Endpoints;
using ADN_pay.Api.Middleware;
using ADN_pay.Api.Services;
using ADN_pay.Api.Tokens;
using ADN_pay.Data;
using ADN_pay.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- LOGGING ---
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "adnapi-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Error)
    .CreateLogger();
builder.Host.UseSerilog();

// --- JWT SECRET ---
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"];
if (string.IsNullOrEmpty(jwtSecret))
{
    jwtSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    Log.Warning("JWT_SECRET non configuré — clé aléatoire générée (les tokens ne survivront pas au redémarrage). Configurez JWT_SECRET.");
}
builder.Configuration["Jwt:Secret"] = jwtSecret;

// --- BASE DE DONNÉES ---
// En service Windows le CWD est System32 ; on ancre sur le même dossier que ADN_pay (dossier parent).
var baseDir = AppContext.BaseDirectory;
var dbPath = Path.Combine(baseDir, "AdnPayData.db");
builder.Services.AddDbContextFactory<BankDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

// --- JWT AUTH ---
var tokenSvc = new JwtTokenService(builder.Configuration);
builder.Services.AddSingleton(tokenSvc);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => opt.TokenValidationParameters = tokenSvc.GetValidationParameters());
builder.Services.AddAuthorization();

// --- CORS (mobile) ---
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// --- SERVICES MÉTIER (même DI que le web) ---
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<ADN_pay.Services.AccountService>();
builder.Services.AddScoped<SavingsService>();
builder.Services.AddScoped<CreditService>();
builder.Services.AddScoped<NotificationHistoryService>();

// Middleware IMiddleware (résolu depuis le scope DI par requête)
builder.Services.AddScoped<UserContextMiddleware>();

// IEmailSender minimal (log uniquement pour l'API — l'envoi d'e-mail reste dans le web)
builder.Services.AddSingleton<IEmailSender, LogEmailSender>();

builder.Services.AddHttpContextAccessor();
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

// --- MIGRATIONS / TABLE RefreshTokens ---
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
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(opt => opt.SwaggerEndpoint("/swagger/v1/swagger.json", "ADN_pay API v1"));

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UserContextMiddleware>();

// --- ENDPOINTS ---
AuthEndpoints.Map(app);
AccountEndpoints.Map(app);
SavingsEndpoints.Map(app);
CreditEndpoints.Map(app);

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }))
   .WithTags("Health");

app.Run();
