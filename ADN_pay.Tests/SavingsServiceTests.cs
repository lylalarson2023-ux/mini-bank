using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

public class SavingsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly IDbContextFactory<BankDbContext> _factory;
    private readonly SavingsService _service;
    private readonly UserContext _user;

    public SavingsServiceTests()
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
        _service = new SavingsService(_factory, _user, NullLogger<SavingsService>.Instance, notifHist);

        // Seed — montants en centimes (ADR-001)
        _db.UserProfiles.AddRange(
            new UserProfile { Id = 1, Email = "user1@test.ma", Nom = "User1", Prenom = "Test", Solde = 100_000L }, // 1000 DH
            new UserProfile { Id = 2, Email = "user2@test.ma", Nom = "User2", Prenom = "Test", Solde = 100_000L }  // 1000 DH
        );
        _db.SaveChanges();

        _user.Profil = _db.UserProfiles.Find(1)!;
        _user.EstConnecte = true;
    }

    private long GetSolde(int userId) { _db.ChangeTracker.Clear(); return _db.UserProfiles.Find(userId)!.Solde; }
    private SavingsPocket? GetPocket(int id) { _db.ChangeTracker.Clear(); return _db.SavingsPockets.Find(id); }
    private SavingsPocket? FirstPocket() { _db.ChangeTracker.Clear(); return _db.SavingsPockets.FirstOrDefault(); }
    private int PocketCount() { _db.ChangeTracker.Clear(); return _db.SavingsPockets.Count(); }

    [Fact]
    public async Task CreerPocheEpargne_SoldeSuffisant_CreeEtDebite()
    {
        var result = await _service.CreerPocheEpargne("Voyage", 20_000L, DateTime.Now.AddMonths(6)); // 200 DH

        Assert.True(result);
        Assert.Equal(80_000L, GetSolde(1)); // 800 DH
        Assert.Equal(1, PocketCount());
    }

    [Fact]
    public async Task CreerPocheEpargne_SoldeInsuffisant_RetourneFalse()
    {
        var result = await _service.CreerPocheEpargne("Voyage", 999_900L, DateTime.Now.AddMonths(6)); // 9999 DH

        Assert.False(result);
        Assert.Equal(100_000L, GetSolde(1)); // inchangé
        Assert.Equal(0, PocketCount());
    }

    [Fact]
    public async Task CasserPocheEpargne_PocheAppartientUser_RemetSolde()
    {
        await _service.CreerPocheEpargne("Voyage", 20_000L, DateTime.Now.AddMonths(6)); // 200 DH
        var pocket = FirstPocket()!;

        var result = await _service.CasserPocheEpargne(pocket.Id);

        Assert.True(result);
        Assert.Equal(100_000L, GetSolde(1)); // 800 + 200 = 1000 DH
        Assert.Equal(0, PocketCount());
    }

    [Fact]
    public async Task CasserPocheEpargne_PocheAutreUser_RetourneFalse()
    {
        _db.SavingsPockets.Add(new SavingsPocket
        {
            Id = 99,
            UserId = 2,
            Objectif = "Autre",
            MontantActuel = 50_000L // 500 DH
        });
        _db.SaveChanges();

        var result = await _service.CasserPocheEpargne(99);

        Assert.False(result);
        Assert.Equal(100_000L, GetSolde(1)); // inchangé
        Assert.Equal(1, PocketCount());
    }

    [Fact]
    public async Task BoosterPocheAsync_PocheAppartientUser_Booste()
    {
        await _service.CreerPocheEpargne("Voyage", 20_000L, DateTime.Now.AddMonths(6)); // 200 DH
        var pocket = FirstPocket()!;

        var result = await _service.BoosterPocheAsync(pocket.Id, 10_000L); // 100 DH

        Assert.True(result);
        Assert.Equal(70_000L, GetSolde(1));                   // 1000 - 200 - 100 = 700 DH
        Assert.Equal(30_000L, GetPocket(pocket.Id)!.MontantActuel); // 200 + 100 = 300 DH
    }

    [Fact]
    public async Task BoosterPocheAsync_PocheAutreUser_RetourneFalse()
    {
        _db.SavingsPockets.Add(new SavingsPocket
        {
            Id = 99,
            UserId = 2,
            Objectif = "Autre",
            MontantActuel = 50_000L // 500 DH
        });
        _db.SaveChanges();

        var result = await _service.BoosterPocheAsync(99, 10_000L); // 100 DH

        Assert.False(result);
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
