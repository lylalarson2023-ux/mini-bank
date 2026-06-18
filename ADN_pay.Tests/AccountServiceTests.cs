using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

public class AccountServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly IDbContextFactory<BankDbContext> _factory;
    private readonly AccountService _service;
    private readonly UserContext _user;

    public AccountServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BankDbContext(options);
        _db.Database.EnsureCreated();

        _factory = new TestDbContextFactory(options);
        _user = new UserContext();
        var notifHist = new NotificationHistoryService(_factory, _user);
        var email = new LogEmailSender(NullLogger<LogEmailSender>.Instance);
        _service = new AccountService(_factory, _user, NullLogger<AccountService>.Instance, notifHist, email);

        // Seed : 2 users — montants en centimes (ADR-001)
        _db.UserProfiles.AddRange(
            new UserProfile { Id = 1, Email = "sender@test.ma",    Nom = "Sender",    Prenom = "Test", Solde = 50_000L  },  // 500 DH
            new UserProfile { Id = 2, Email = "recipient@test.ma", Nom = "Recipient", Prenom = "Test", Solde = 20_000L  }   // 200 DH
        );
        _db.SaveChanges();
        _user.Profil = _db.UserProfiles.Find(1)!;
        _user.EstConnecte = true;
    }

    private long GetSolde(int userId)
    {
        _db.ChangeTracker.Clear();
        return _db.UserProfiles.Find(userId)!.Solde;
    }

    [Fact]
    public async Task EffectuerVirementAsync_DebiteEnvoyeur_CrediteDestinataire()
    {
        var result = await _service.EffectuerVirementAsync("recipient@test.ma", 10_000L, "Test"); // 100 DH

        Assert.True(result);
        Assert.Equal(40_000L, GetSolde(1)); // 400 DH
        Assert.Equal(30_000L, GetSolde(2)); // 300 DH
    }

    [Fact]
    public async Task EffectuerVirementAsync_SoldeInsuffisant_RetourneFalse()
    {
        var result = await _service.EffectuerVirementAsync("recipient@test.ma", 999_900L, "Test"); // 9999 DH

        Assert.False(result);
        Assert.Equal(50_000L, GetSolde(1)); // inchangé
        Assert.Equal(20_000L, GetSolde(2)); // inchangé
    }

    [Fact]
    public async Task EffectuerVirementAsync_DestinataireInexistant_RetourneFalse()
    {
        var result = await _service.EffectuerVirementAsync("nobody@test.ma", 10_000L, "Test");

        Assert.False(result);
    }

    [Fact]
    public async Task ExecuterOperationAsync_Depot_AjouteSolde()
    {
        _user.Profil = _db.UserProfiles.Find(1)!;
        var result = await _service.ExecuterOperationAsync(30_000L, "Test dépôt", "DÉPÔT"); // 300 DH

        Assert.True(result);
        Assert.Equal(80_000L, GetSolde(1)); // 800 DH
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
