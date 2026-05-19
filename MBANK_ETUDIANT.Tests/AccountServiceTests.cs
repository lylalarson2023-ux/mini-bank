using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;
using MBANK_ETUDIANT.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MBANK_ETUDIANT.Tests;

public class AccountServiceTests : IDisposable
{
    private readonly BankDbContext _db;
    private readonly AccountService _service;
    private readonly UserContext _user;

    public AccountServiceTests()
    {
        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new BankDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _user = new UserContext();
        _service = new AccountService(_db, _user, NullLogger<AccountService>.Instance);

        // Seed : 2 users
        _db.UserProfiles.AddRange(
            new UserProfile { Id = 1, Email = "sender@test.ma", Nom = "Sender", Prenom = "Test", Solde = 500m },
            new UserProfile { Id = 2, Email = "recipient@test.ma", Nom = "Recipient", Prenom = "Test", Solde = 200m }
        );
        _db.SaveChanges();
        _user.Profil = _db.UserProfiles.Find(1)!;
        _user.EstConnecte = true;
    }

    [Fact]
    public async Task EffectuerVirementAsync_DebiteEnvoyeur_CrediteDestinataire()
    {
        var result = await _service.EffectuerVirementAsync("recipient@test.ma", 100m, "Test");

        Assert.True(result);
        Assert.Equal(400m, _db.UserProfiles.Find(1)!.Solde);
        Assert.Equal(300m, _db.UserProfiles.Find(2)!.Solde);
    }

    [Fact]
    public async Task EffectuerVirementAsync_SoldeInsuffisant_RetourneFalse()
    {
        var result = await _service.EffectuerVirementAsync("recipient@test.ma", 9999m, "Test");

        Assert.False(result);
        Assert.Equal(500m, _db.UserProfiles.Find(1)!.Solde);
        Assert.Equal(200m, _db.UserProfiles.Find(2)!.Solde);
    }

    [Fact]
    public async Task EffectuerVirementAsync_DestinataireInexistant_RetourneFalse()
    {
        var result = await _service.EffectuerVirementAsync("nobody@test.ma", 100m, "Test");

        Assert.False(result);
    }

    [Fact]
    public async Task ExecuterOperationAsync_Depot_AjouteSolde()
    {
        _user.Profil = _db.UserProfiles.Find(1)!;
        var result = await _service.ExecuterOperationAsync(300m, "Test dépôt", "DÉPÔT");

        Assert.True(result);
        Assert.Equal(800m, _db.UserProfiles.Find(1)!.Solde);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}
