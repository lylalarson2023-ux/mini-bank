using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

// Vérification de l'adresse e-mail à l'inscription : nouveau compte non vérifié + code,
// confirmation par code (valide/faux/expiré/déjà vérifié), renvoi de code.
public class AuthServiceEmailVerificationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly AuthService _service;

    public AuthServiceEmailVerificationTests()
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
    }

    private UserProfile? Find(string email) { _db.ChangeTracker.Clear(); return _db.UserProfiles.FirstOrDefault(u => u.Email == email); }

    private void Seed(string email, bool verifie, string? codeClair, DateTime? expiry)
    {
        _db.UserProfiles.Add(new UserProfile
        {
            Email = email,
            Nom = "Test",
            Prenom = "User",
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword("AncienMdp1!"),
            EmailVerifie = verifie,
            EmailVerifCodeHash = codeClair == null ? null : BCrypt.Net.BCrypt.HashPassword(codeClair),
            EmailVerifCodeExpiry = expiry
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task CreerNouveauCompte_DemarreNonVerifieAvecUnCode()
    {
        var u = new UserProfile { Email = "new@test.ma", Nom = "New", Prenom = "User" };
        var (ok, _) = await _service.CreerNouveauCompte(u, "NouveauMdp1!");

        Assert.True(ok);
        var saved = Find("new@test.ma")!;
        Assert.False(saved.EmailVerifie);
        Assert.False(string.IsNullOrEmpty(saved.EmailVerifCodeHash));
        Assert.NotNull(saved.EmailVerifCodeExpiry);
    }

    [Fact]
    public async Task VerifyEmailCode_CodeValide_MarqueVerifieEtEffaceLeCode()
    {
        Seed("v@test.ma", verifie: false, codeClair: "123456", expiry: DateTime.UtcNow.AddMinutes(20));

        var (ok, _) = await _service.VerifyEmailCodeAsync("v@test.ma", "123456");

        Assert.True(ok);
        var after = Find("v@test.ma")!;
        Assert.True(after.EmailVerifie);
        Assert.Null(after.EmailVerifCodeHash);
    }

    [Fact]
    public async Task VerifyEmailCode_CodeIncorrect_Refuse()
    {
        Seed("w@test.ma", verifie: false, codeClair: "123456", expiry: DateTime.UtcNow.AddMinutes(20));

        var (ok, _) = await _service.VerifyEmailCodeAsync("w@test.ma", "000000");

        Assert.False(ok);
        Assert.False(Find("w@test.ma")!.EmailVerifie);
    }

    [Fact]
    public async Task VerifyEmailCode_CodeExpire_Refuse()
    {
        Seed("x@test.ma", verifie: false, codeClair: "123456", expiry: DateTime.UtcNow.AddMinutes(-1));

        var (ok, message) = await _service.VerifyEmailCodeAsync("x@test.ma", "123456");

        Assert.False(ok);
        Assert.Contains("expiré", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyEmailCode_DejaVerifie_RetourneSucces()
    {
        Seed("y@test.ma", verifie: true, codeClair: null, expiry: null);

        var (ok, _) = await _service.VerifyEmailCodeAsync("y@test.ma", "123456");

        Assert.True(ok);
    }

    [Fact]
    public async Task ResendEmailVerification_CompteNonVerifie_GenereUnNouveauCode()
    {
        Seed("r@test.ma", verifie: false, codeClair: null, expiry: null);

        await _service.ResendEmailVerificationAsync("r@test.ma");

        Assert.False(string.IsNullOrEmpty(Find("r@test.ma")!.EmailVerifCodeHash));
    }

    [Fact]
    public async Task ResendEmailVerification_DejaVerifie_NeGenerePasDeCode()
    {
        Seed("s@test.ma", verifie: true, codeClair: null, expiry: null);

        await _service.ResendEmailVerificationAsync("s@test.ma");

        Assert.Null(Find("s@test.ma")!.EmailVerifCodeHash);
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
