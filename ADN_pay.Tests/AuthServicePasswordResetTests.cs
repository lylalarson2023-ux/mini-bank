using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

// Flux « mot de passe oublié » (pré-authentification, par code à 6 chiffres) :
// demande générique (anti-énumération), réinitialisation par code valide/expiré/faux,
// et révocation des sessions (rotation du SecurityStamp).
public class AuthServicePasswordResetTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly AuthService _service;

    public AuthServicePasswordResetTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BankDbContext>().UseSqlite(_connection).Options;
        _db = new BankDbContext(options);
        _db.Database.EnsureCreated();

        var factory = new TestDbContextFactory(options);
        var user = new UserContext();
        var notifHist = new NotificationHistoryService(factory, user);
        var email = new LogEmailSender(NullLogger<LogEmailSender>.Instance);
        var config = new ConfigurationBuilder().Build();
        _service = new AuthService(factory, user, NullLogger<AuthService>.Instance,
            new HttpContextAccessor(), notifHist, email, config);

        _db.UserProfiles.Add(new UserProfile
        {
            Id = 1,
            Email = "client@test.ma",
            Nom = "Client",
            Prenom = "Test",
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword("AncienMdp1!")
        });
        _db.SaveChanges();
    }

    private UserProfile GetUser(int id) { _db.ChangeTracker.Clear(); return _db.UserProfiles.Find(id)!; }

    // ─────────────────────────── Demande de code ───────────────────────────

    [Fact]
    public async Task RequestPasswordReset_CompteExistant_StockeUnCodeHacheEtExpiration()
    {
        var msg = await _service.RequestPasswordResetAsync("client@test.ma");

        var u = GetUser(1);
        Assert.False(string.IsNullOrEmpty(u.PasswordResetCodeHash));
        Assert.NotNull(u.PasswordResetCodeExpiry);
        Assert.True(u.PasswordResetCodeExpiry > DateTime.UtcNow);
        Assert.Contains("code", msg, StringComparison.OrdinalIgnoreCase); // message générique
    }

    [Fact]
    public async Task RequestPasswordReset_EmailInconnu_ResteGeneriqueSansThrow()
    {
        var msg = await _service.RequestPasswordResetAsync("inconnu@test.ma");

        Assert.False(string.IsNullOrWhiteSpace(msg)); // même message générique, aucune exception
    }

    [Fact]
    public async Task RequestPasswordReset_CompteBloque_NeGenerePasDeCode()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.Bloque = true;
        _db.SaveChanges();

        await _service.RequestPasswordResetAsync("client@test.ma");

        Assert.Null(GetUser(1).PasswordResetCodeHash);
    }

    // ─────────────────────────── Réinitialisation par code ───────────────────────────

    [Fact]
    public async Task ResetPasswordWithCode_CodeValide_ChangeLeMotDePasseEtRevoqueLesSessions()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.PasswordResetCodeHash = BCrypt.Net.BCrypt.HashPassword("123456");
        u.PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(10);
        u.SecurityStamp = "OLDSTAMP";
        _db.SaveChanges();

        var (ok, _) = await _service.ResetPasswordWithCodeAsync("client@test.ma", "123456", "NouveauMdp1!");

        Assert.True(ok);
        var after = GetUser(1);
        Assert.True(BCrypt.Net.BCrypt.Verify("NouveauMdp1!", after.MotDePasseHash));   // le nouveau marche
        Assert.False(BCrypt.Net.BCrypt.Verify("AncienMdp1!", after.MotDePasseHash));   // l'ancien ne marche plus
        Assert.Null(after.PasswordResetCodeHash);                                       // code consommé
        Assert.NotEqual("OLDSTAMP", after.SecurityStamp);                              // sessions révoquées
    }

    [Fact]
    public async Task ResetPasswordWithCode_CodeIncorrect_Refuse()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.PasswordResetCodeHash = BCrypt.Net.BCrypt.HashPassword("123456");
        u.PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(10);
        _db.SaveChanges();

        var (ok, _) = await _service.ResetPasswordWithCodeAsync("client@test.ma", "000000", "NouveauMdp1!");

        Assert.False(ok);
        Assert.True(BCrypt.Net.BCrypt.Verify("AncienMdp1!", GetUser(1).MotDePasseHash)); // inchangé
    }

    [Fact]
    public async Task ResetPasswordWithCode_CodeExpire_Refuse()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.PasswordResetCodeHash = BCrypt.Net.BCrypt.HashPassword("123456");
        u.PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(-1); // déjà expiré
        _db.SaveChanges();

        var (ok, message) = await _service.ResetPasswordWithCodeAsync("client@test.ma", "123456", "NouveauMdp1!");

        Assert.False(ok);
        Assert.Contains("expiré", message, StringComparison.OrdinalIgnoreCase);
        Assert.True(BCrypt.Net.BCrypt.Verify("AncienMdp1!", GetUser(1).MotDePasseHash)); // inchangé
    }

    [Fact]
    public async Task ResetPasswordWithCode_MotDePasseFaible_Refuse()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.PasswordResetCodeHash = BCrypt.Net.BCrypt.HashPassword("123456");
        u.PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(10);
        _db.SaveChanges();

        var (ok, _) = await _service.ResetPasswordWithCodeAsync("client@test.ma", "123456", "faible");

        Assert.False(ok);
        Assert.NotNull(GetUser(1).PasswordResetCodeHash); // code non consommé (échec avant)
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestDbContextFactory(DbContextOptions<BankDbContext> options) : IDbContextFactory<BankDbContext>
    {
        public BankDbContext CreateDbContext() => new(options);
        public Task<BankDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BankDbContext(options));
    }
}
