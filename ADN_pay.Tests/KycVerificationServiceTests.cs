using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;

namespace ADN_pay.Tests;

// Contrôles automatiques de cohérence / anti-fraude sur un dossier KYC :
// doublons (CIN, téléphone, identité), pièces manquantes, âge, e-mail non vérifié.
public class KycVerificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly KycVerificationService _service;

    public KycVerificationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<BankDbContext>().UseSqlite(_connection).Options;
        _db = new BankDbContext(options);
        _db.Database.EnsureCreated();
        _service = new KycVerificationService(new TestDbContextFactory(options));
    }

    // Dossier « propre » complet : e-mail vérifié, CIN + téléphone uniques, âge 20 ans, pièces présentes.
    private UserProfile CandidatPropre(int id = 1) => new()
    {
        Id = id,
        Email = $"cand{id}@test.ma",
        Nom = "Alaoui",
        Prenom = "Sara",
        Telephone = "0612345678",
        PassportOuCIN = "AB123456",
        DateNaissance = DateTime.Today.AddYears(-20),
        DocIdentiteUrl = "https://x/id.png",
        SelfieUrl = "https://x/selfie.png",
        EmailVerifie = true
    };

    [Fact]
    public async Task Analyser_DossierPropre_AucuneAnomalie()
    {
        _db.UserProfiles.Add(CandidatPropre());
        _db.SaveChanges();

        var flags = await _service.AnalyserAsync(1);

        Assert.Empty(flags);
    }

    [Fact]
    public async Task Analyser_CinDuplique_Alerte()
    {
        var c = CandidatPropre(1);
        var autre = CandidatPropre(2);
        autre.Email = "autre@test.ma"; autre.Telephone = "0698765432"; // seul le CIN est partagé
        _db.UserProfiles.AddRange(c, autre);
        _db.SaveChanges();

        var flags = await _service.AnalyserAsync(1);

        Assert.Contains(flags, f => f.Severite == KycSeverite.Alerte && f.Message.Contains("CIN/Passeport"));
    }

    [Fact]
    public async Task Analyser_TelephoneDuplique_Attention()
    {
        var c = CandidatPropre(1);
        var autre = CandidatPropre(2);
        autre.Email = "autre@test.ma"; autre.PassportOuCIN = "ZZ999999"; // seul le téléphone est partagé
        _db.UserProfiles.AddRange(c, autre);
        _db.SaveChanges();

        var flags = await _service.AnalyserAsync(1);

        Assert.Contains(flags, f => f.Severite == KycSeverite.Attention && f.Message.Contains("Téléphone"));
    }

    [Fact]
    public async Task Analyser_PiecesManquantes_Signale()
    {
        var c = CandidatPropre();
        c.DocIdentiteUrl = "";
        c.SelfieUrl = "";
        _db.UserProfiles.Add(c);
        _db.SaveChanges();

        var flags = await _service.AnalyserAsync(1);

        Assert.Contains(flags, f => f.Message.Contains("pièce d'identité"));
        Assert.Contains(flags, f => f.Message.Contains("Selfie"));
    }

    [Fact]
    public async Task Analyser_Mineur_Alerte()
    {
        var c = CandidatPropre();
        c.DateNaissance = DateTime.Today.AddYears(-12);
        _db.UserProfiles.Add(c);
        _db.SaveChanges();

        var flags = await _service.AnalyserAsync(1);

        Assert.Contains(flags, f => f.Severite == KycSeverite.Alerte && f.Message.Contains("15 ans"));
    }

    [Fact]
    public async Task Analyser_EmailNonVerifie_Attention()
    {
        var c = CandidatPropre();
        c.EmailVerifie = false;
        _db.UserProfiles.Add(c);
        _db.SaveChanges();

        var flags = await _service.AnalyserAsync(1);

        Assert.Contains(flags, f => f.Message.Contains("e-mail non vérifiée"));
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
