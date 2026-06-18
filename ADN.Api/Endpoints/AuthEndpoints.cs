using System.Security.Claims;
using ADN_pay.Api.Tokens;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ADN_pay.Api.Endpoints;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth");
        g.MapPost("/login", Login);
        g.MapPost("/refresh", Refresh);
        g.MapPost("/logout", Logout).RequireAuthorization();
        g.MapPost("/register", Register);
    }

    private record LoginRequest(string Email, string Password);
    private record RegisterRequest(string Email, string Password, string Nom, string Prenom, string? Telephone);
    private record RefreshRequest(string RefreshToken);
    private record LogoutRequest(string RefreshToken);

    private static object MapUser(UserProfile u) => new
    {
        u.Id, u.Email, u.Prenom, u.Nom, u.Telephone,
        Solde = u.Solde,
        SoldeDh = (u.Solde / 100m).ToString("0.00"),
        Statut = u.Statut.ToString(),
        u.IsAdmin,
        u.TwoFactorEnabled,
        DateInscription = u.DateInscription.ToString("O"),
    };

    private static async Task<IResult> Login(
        LoginRequest req,
        IDbContextFactory<BankDbContext> dbFactory,
        JwtTokenService jwt,
        ILogger<Program> log)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new { error = "Email et mot de passe requis." });

        await using var db = await dbFactory.CreateDbContextAsync();
        var emailLower = req.Email.Trim().ToLower();
        var user = await db.UserProfiles.FirstOrDefaultAsync(u => u.Email == emailLower);

        if (user == null || string.IsNullOrEmpty(user.MotDePasseHash) || !BCrypt.Net.BCrypt.Verify(req.Password, user.MotDePasseHash))
        {
            db.UserLogins.Add(new UserLogin
            {
                UserId = user?.Id, Email = req.Email, Date = DateTime.UtcNow,
                Success = false, FailureReason = "Identifiants invalides",
                IpAddress = "api", UserAgent = "api"
            });
            await db.SaveChangesAsync();
            return Results.Unauthorized();
        }

        if (user.CompteCloture)
        {
            db.UserLogins.Add(new UserLogin
            {
                UserId = user.Id, Email = req.Email, Date = DateTime.UtcNow,
                Success = false, FailureReason = "Compte clôturé",
                IpAddress = "api", UserAgent = "api"
            });
            await db.SaveChangesAsync();
            return Results.Forbid();
        }

        var rawRefresh = jwt.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = jwt.HashToken(rawRefresh),
            ExpiresAt = DateTime.UtcNow.AddDays(JwtTokenService.RefreshTokenDays),
        });
        db.UserLogins.Add(new UserLogin
        {
            UserId = user.Id, Email = user.Email, Date = DateTime.UtcNow,
            Success = true, IpAddress = "api", UserAgent = "api"
        });
        await db.SaveChangesAsync();

        log.LogInformation("API login réussi : {Email}", PiiMasker.MaskEmail(user.Email));
        return Results.Ok(new
        {
            accessToken = jwt.GenerateAccessToken(user),
            refreshToken = rawRefresh,
            expiresIn = JwtTokenService.AccessTokenMinutes * 60,
            user = MapUser(user),
        });
    }

    private static async Task<IResult> Refresh(
        RefreshRequest req,
        IDbContextFactory<BankDbContext> dbFactory,
        JwtTokenService jwt)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return Results.BadRequest(new { error = "refreshToken requis." });

        await using var db = await dbFactory.CreateDbContextAsync();
        var hash = jwt.HashToken(req.RefreshToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.Revoked);

        if (stored == null || stored.ExpiresAt < DateTime.UtcNow)
            return Results.Unauthorized();

        var user = await db.UserProfiles.FindAsync(stored.UserId);
        if (user == null || user.CompteCloture)
            return Results.Unauthorized();

        // Rotation : révoque l'ancien, émet un nouveau
        stored.Revoked = true;
        var rawNew = jwt.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = jwt.HashToken(rawNew),
            ExpiresAt = DateTime.UtcNow.AddDays(JwtTokenService.RefreshTokenDays),
        });
        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            accessToken = jwt.GenerateAccessToken(user),
            refreshToken = rawNew,
            expiresIn = JwtTokenService.AccessTokenMinutes * 60,
        });
    }

    private static async Task<IResult> Logout(
        LogoutRequest req,
        IDbContextFactory<BankDbContext> dbFactory,
        JwtTokenService jwt,
        ClaimsPrincipal principal)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return Results.Ok(); // idempotent

        await using var db = await dbFactory.CreateDbContextAsync();
        var hash = jwt.HashToken(req.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored != null)
        {
            stored.Revoked = true;
            await db.SaveChangesAsync();
        }
        return Results.Ok();
    }

    private static async Task<IResult> Register(
        RegisterRequest req,
        IDbContextFactory<BankDbContext> dbFactory,
        JwtTokenService jwt,
        ILogger<Program> log)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password)
            || string.IsNullOrWhiteSpace(req.Nom) || string.IsNullOrWhiteSpace(req.Prenom))
            return Results.BadRequest(new { error = "Email, mot de passe, nom et prénom requis." });

        if (req.Password.Length < 8
            || !req.Password.Any(char.IsUpper)
            || !req.Password.Any(char.IsLower)
            || !req.Password.Any(char.IsDigit)
            || !req.Password.Any(c => !char.IsLetterOrDigit(c)))
            return Results.BadRequest(new { error = "Mot de passe trop faible (min 8 car, maj, min, chiffre, spécial)." });

        await using var db = await dbFactory.CreateDbContextAsync();
        var emailLower = req.Email.Trim().ToLower();
        if (await db.UserProfiles.AnyAsync(u => u.Email == emailLower))
            return Results.Conflict(new { error = "Cet email est déjà utilisé." });

        var user = new UserProfile
        {
            Email = emailLower,
            Nom = req.Nom.Trim(),
            Prenom = req.Prenom.Trim(),
            Telephone = req.Telephone?.Trim() ?? "",
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            MotDePasse = "",
        };
        db.UserProfiles.Add(user);
        await db.SaveChangesAsync();

        var rawRefresh = jwt.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = jwt.HashToken(rawRefresh),
            ExpiresAt = DateTime.UtcNow.AddDays(JwtTokenService.RefreshTokenDays),
        });
        await db.SaveChangesAsync();

        log.LogInformation("API inscription : {Email}", PiiMasker.MaskEmail(user.Email));
        return Results.Created($"/api/v1/account/me", new
        {
            accessToken = jwt.GenerateAccessToken(user),
            refreshToken = rawRefresh,
            expiresIn = JwtTokenService.AccessTokenMinutes * 60,
            user = MapUser(user),
        });
    }
}
