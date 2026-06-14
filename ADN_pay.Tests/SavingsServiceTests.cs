using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

public class SavingsServiceTests : IDisposable
{
    private readonly BankDbContext _db;
    private readonly SavingsService _service;
    private readonly UserContext _user;

    public SavingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new BankDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _user = new UserContext();
        var notifHist = new NotificationHistoryService(_db, _user);
        _service = new SavingsService(_db, _user, NullLogger<SavingsService>.Instance, notifHist);

        // Seed — montants en centimes (ADR-001)
        _db.UserProfiles.AddRange(
            new UserProfile { Id = 1, Email = "user1@test.ma", Nom = "User1", Prenom = "Test", Solde = 100_000L }, // 1000 DH
            new UserProfile { Id = 2, Email = "user2@test.ma", Nom = "User2", Prenom = "Test", Solde = 100_000L }  // 1000 DH
        );
        _db.SaveChanges();

        _user.Profil = _db.UserProfiles.Find(1)!;
        _user.EstConnecte = true;
    }

    [Fact]
    public async Task CreerPocheEpargne_SoldeSuffisant_CreeEtDebite()
    {
        var result = await _service.CreerPocheEpargne("Voyage", 20_000L, DateTime.Now.AddMonths(6)); // 200 DH

        Assert.True(result);
        Assert.Equal(80_000L, _db.UserProfiles.Find(1)!.Solde); // 800 DH
        Assert.Single(_db.SavingsPockets);
    }

    [Fact]
    public async Task CreerPocheEpargne_SoldeInsuffisant_RetourneFalse()
    {
        var result = await _service.CreerPocheEpargne("Voyage", 999_900L, DateTime.Now.AddMonths(6)); // 9999 DH

        Assert.False(result);
        Assert.Equal(100_000L, _db.UserProfiles.Find(1)!.Solde); // inchangé
        Assert.Empty(_db.SavingsPockets);
    }

    [Fact]
    public async Task CasserPocheEpargne_PocheAppartientUser_RemetSolde()
    {
        await _service.CreerPocheEpargne("Voyage", 20_000L, DateTime.Now.AddMonths(6)); // 200 DH
        var pocket = _db.SavingsPockets.First();

        var result = await _service.CasserPocheEpargne(pocket.Id);

        Assert.True(result);
        Assert.Equal(100_000L, _db.UserProfiles.Find(1)!.Solde); // 800 + 200 = 1000 DH
        Assert.Empty(_db.SavingsPockets);
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
        Assert.Equal(100_000L, _db.UserProfiles.Find(1)!.Solde); // inchangé
        Assert.Single(_db.SavingsPockets);
    }

    [Fact]
    public async Task BoosterPocheAsync_PocheAppartientUser_Booste()
    {
        await _service.CreerPocheEpargne("Voyage", 20_000L, DateTime.Now.AddMonths(6)); // 200 DH
        var pocket = _db.SavingsPockets.First();

        var result = await _service.BoosterPocheAsync(pocket.Id, 10_000L); // 100 DH

        Assert.True(result);
        Assert.Equal(70_000L, _db.UserProfiles.Find(1)!.Solde);         // 1000 - 200 - 100 = 700 DH
        Assert.Equal(30_000L, _db.SavingsPockets.First().MontantActuel); // 200 + 100 = 300 DH
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
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}
