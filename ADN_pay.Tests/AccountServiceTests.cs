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

    [Fact]
    public async Task EffectuerVirementAsync_MontantNegatif_RetourneFalse()
    {
        var result = await _service.EffectuerVirementAsync("recipient@test.ma", -10_000L, "Hack");

        Assert.False(result);
        Assert.Equal(50_000L, GetSolde(1)); // inchangé (pas de création d'argent)
        Assert.Equal(20_000L, GetSolde(2)); // inchangé
    }

    [Fact]
    public async Task UpdatePlafondsAsync_AuDelaDuMaxDuStatut_RetourneFalse()
    {
        // User 1 = STANDARD → max 5 000 / 50 000 DH (500 000 / 5 000 000 centimes)
        var (ok, _) = await _service.UpdatePlafondsAsync(600_000L, 5_000_000L); // journalier > max
        Assert.False(ok);
    }

    [Fact]
    public async Task UpdatePlafondsAsync_DansLaLimiteDuStatut_Reussit()
    {
        var (ok, _) = await _service.UpdatePlafondsAsync(500_000L, 5_000_000L); // = max STANDARD
        Assert.True(ok);

        _db.ChangeTracker.Clear();
        var u = _db.UserProfiles.Find(1)!;
        Assert.Equal(500_000L, u.PlafondJournalier);
        Assert.Equal(5_000_000L, u.PlafondMensuel);
    }

    [Fact]
    public void PlafondsMaxPourStatut_VipSuperieurAStandard()
    {
        var std = AccountService.PlafondsMaxPourStatut(UserStatus.STANDARD);
        var vip = AccountService.PlafondsMaxPourStatut(UserStatus.VIP);
        Assert.True(vip.Journalier > std.Journalier);
        Assert.True(vip.Mensuel > std.Mensuel);
    }

    // --- DESIGN DE CARTE (galerie, déblocage cumulatif) ---

    private string GetCarteDesign(int userId)
    {
        _db.ChangeTracker.Clear();
        return _db.UserProfiles.Find(userId)!.CarteDesign;
    }

    [Fact]
    public async Task ChangerCarteDesignAsync_DesignStandard_Persiste()
    {
        var (ok, _) = await _service.ChangerCarteDesignAsync("menthe-vif");

        Assert.True(ok);
        Assert.Equal("menthe-vif", GetCarteDesign(1));
        Assert.Equal("menthe-vif", _user.Profil.CarteDesign); // session à jour
    }

    [Fact]
    public async Task ChangerCarteDesignAsync_DesignPremium_RefusePourStandard()
    {
        // User 1 = STANDARD → "gold" (Premium) verrouillé
        var (ok, _) = await _service.ChangerCarteDesignAsync("gold");

        Assert.False(ok);
        Assert.Equal("", GetCarteDesign(1)); // inchangé
    }

    [Fact]
    public async Task ChangerCarteDesignAsync_DesignInconnu_Refuse()
    {
        var (ok, _) = await _service.ChangerCarteDesignAsync("licorne");

        Assert.False(ok);
        Assert.Equal("", GetCarteDesign(1));
    }

    [Fact]
    public async Task ChangerCarteDesignAsync_DeblocageCumulatif_VipAccedeATout()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.Statut = UserStatus.VIP;
        _db.SaveChanges();

        var (okStd, _)  = await _service.ChangerCarteDesignAsync("cuivre");  // Standard
        var (okPrem, _) = await _service.ChangerCarteDesignAsync("gold");    // Premium
        var (okVip, _)  = await _service.ChangerCarteDesignAsync("noir-or"); // VIP

        Assert.True(okStd);
        Assert.True(okPrem);
        Assert.True(okVip);
        Assert.Equal("noir-or", GetCarteDesign(1)); // dernier appliqué
    }

    // --- KYC PREMIUM ADAPTÉ AU STATUT (Travailleur/Étudiant) ---

    [Fact]
    public async Task SoumettreDossierKYC_PersisteChampsStatut_EtPreserveDomicileAnterieur()
    {
        var u0 = _db.UserProfiles.Find(1)!;
        u0.DocDomicileUrl = "docs/ancien-domicile.pdf"; // justificatif d'un dossier antérieur
        _db.SaveChanges();

        var kyc = new UserProfile
        {
            Nom = "Sender", Prenom = "Test",
            PassportOuCIN = "AB123456",
            AdresseCasablanca = "12 rue Test",
            Telephone = "0612345678",
            SituationMatrimoniale = "CELIBATAIRE",
            StatutKyc = "TRAVAILLEUR",
            Profession = "Développeur",
            Employeur = "ADN Corp",
            Secteur = "TECH",
            TrancheRevenu = "3000-6000 DH",
            SourceFonds = "SALAIRE",
            UrgenceNom = "Contact Test",
            UrgenceTelephone = "0698765432",
            DocIdentiteUrl = "docs/cin.pdf",
            SelfieUrl = "docs/selfie.jpg",
            CguAcceptees = true
        };

        var ok = await _service.SoumettreDossierKYC(kyc);

        Assert.True(ok);
        _db.ChangeTracker.Clear();
        var u = _db.UserProfiles.Find(1)!;
        Assert.Equal("TRAVAILLEUR", u.StatutKyc);
        Assert.Equal("SALAIRE", u.SourceFonds);
        Assert.Equal("ADN Corp", u.Employeur);
        Assert.Equal("3000-6000 DH", u.TrancheRevenu);
        Assert.Equal("Contact Test", u.UrgenceNom);
        Assert.Equal("0698765432", u.UrgenceTelephone);
        Assert.Equal("docs/selfie.jpg", u.SelfieUrl);
        Assert.Equal("docs/ancien-domicile.pdf", u.DocDomicileUrl); // non écrasé
        Assert.True(u.PendingPremiumUpgrade);
        Assert.Equal(45_000L, u.Solde); // 50 DH prélevés
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
