using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;
using MBANK_ETUDIANT.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MBANK_ETUDIANT.Tests;

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
        _service = new SavingsService(_db, _user, NullLogger<SavingsService>.Instance);

        _db.UserProfiles.AddRange(
            new UserProfile { Id = 1, Email = "user1@test.ma", Nom = "User1", Prenom = "Test", Solde = 1000m },
            new UserProfile { Id = 2, Email = "user2@test.ma", Nom = "User2", Prenom = "Test", Solde = 1000m }
        );
        _db.SaveChanges();

        _user.Profil = _db.UserProfiles.Find(1)!;
        _user.EstConnecte = true;
    }

    [Fact]
    public async Task CreerPocheEpargne_SoldeSuffisant_CreeEtDebite()
    {
        var result = await _service.CreerPocheEpargne("Voyage", 200m, DateTime.Now.AddMonths(6));

        Assert.True(result);
        Assert.Equal(800m, _db.UserProfiles.Find(1)!.Solde);
        Assert.Single(_db.SavingsPockets);
    }

    [Fact]
    public async Task CreerPocheEpargne_SoldeInsuffisant_RetourneFalse()
    {
        var result = await _service.CreerPocheEpargne("Voyage", 9999m, DateTime.Now.AddMonths(6));

        Assert.False(result);
        Assert.Equal(1000m, _db.UserProfiles.Find(1)!.Solde);
        Assert.Empty(_db.SavingsPockets);
    }

    [Fact]
    public async Task CasserPocheEpargne_PocheAppartientUser_RemetSolde()
    {
        // Créer une poche pour user 1
        await _service.CreerPocheEpargne("Voyage", 200m, DateTime.Now.AddMonths(6));
        var pocket = _db.SavingsPockets.First();

        var result = await _service.CasserPocheEpargne(pocket.Id);

        Assert.True(result);
        Assert.Equal(1000m, _db.UserProfiles.Find(1)!.Solde); // 800 + 200
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
            MontantActuel = 500m
        });
        _db.SaveChanges();

        var result = await _service.CasserPocheEpargne(99);

        Assert.False(result);
        Assert.Equal(1000m, _db.UserProfiles.Find(1)!.Solde);
        Assert.Single(_db.SavingsPockets);
    }

    [Fact]
    public async Task BoosterPocheAsync_PocheAppartientUser_Booste()
    {
        await _service.CreerPocheEpargne("Voyage", 200m, DateTime.Now.AddMonths(6));
        var pocket = _db.SavingsPockets.First();

        var result = await _service.BoosterPocheAsync(pocket.Id, 100m);

        Assert.True(result);
        Assert.Equal(700m, _db.UserProfiles.Find(1)!.Solde); // 1000 - 200 - 100
        Assert.Equal(300m, _db.SavingsPockets.First().MontantActuel);
    }

    [Fact]
    public async Task BoosterPocheAsync_PocheAutreUser_RetourneFalse()
    {
        _db.SavingsPockets.Add(new SavingsPocket
        {
            Id = 99,
            UserId = 2,
            Objectif = "Autre",
            MontantActuel = 500m
        });
        _db.SaveChanges();

        var result = await _service.BoosterPocheAsync(99, 100m);

        Assert.False(result);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}
