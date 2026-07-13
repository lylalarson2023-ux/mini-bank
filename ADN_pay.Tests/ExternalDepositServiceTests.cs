using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

// Idempotence des dépôts externes (Stripe, Flutterwave, virement) : une référence
// donnée ne peut créditer le compte qu'une seule fois, quel que soit le nombre
// de rejeux (webhook dupliqué, URL de succès rechargée, callback + polling).
public class ExternalDepositServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly ExternalDepositService _service;

    public ExternalDepositServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BankDbContext(options);
        _db.Database.EnsureCreated();

        var factory = new TestDbContextFactory(options);
        var user = new UserContext(); // service indépendant de la session (webhooks)
        var notifHist = new NotificationHistoryService(factory, user);
        _service = new ExternalDepositService(factory, notifHist, NullLogger<ExternalDepositService>.Instance);

        // Seed : 1 client — montants en centimes (ADR-001)
        _db.UserProfiles.Add(new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test", Solde = 10_000L }); // 100 DH
        _db.SaveChanges();
    }

    private UserProfile GetUser(int id) { _db.ChangeTracker.Clear(); return _db.UserProfiles.Find(id)!; }
    private List<Transaction> GetTransactions(int userId) { _db.ChangeTracker.Clear(); return _db.Transactions.Where(t => t.UserId == userId).ToList(); }

    [Fact]
    public async Task Crediter_CrediteSoldeEtCreeTransactionAvecReference()
    {
        var result = await _service.CrediterAsync(1, 25_000L, "stripe", "cs_test_123", "Dépôt par carte Stripe");

        Assert.True(result);
        Assert.Equal(35_000L, GetUser(1).Solde); // 100 + 250 DH
        var tx = Assert.Single(GetTransactions(1));
        Assert.Equal("DÉPÔT", tx.Type);
        Assert.Equal("stripe:cs_test_123", tx.ReferenceExterne);
        Assert.Equal(35_000L, tx.SoldeApres);
    }

    [Fact]
    public async Task Crediter_AvecFrais_ConsigneLesFraisDansLaTransaction()
    {
        await _service.CrediterAsync(1, 25_000L, "virement", "ADN-TESTMM", "Dépôt Mobile Money", 1_560L);

        var tx = Assert.Single(GetTransactions(1));
        Assert.Equal(25_000L, tx.Montant);
        Assert.Equal(1_560L, tx.Frais);
    }

    [Fact]
    public async Task Crediter_SansFrais_FraisNuls()
    {
        await _service.CrediterAsync(1, 25_000L, "stripe", "cs_x", "Dépôt carte");

        Assert.Equal(0L, Assert.Single(GetTransactions(1)).Frais);
    }

    [Fact]
    public async Task Crediter_MemeReference_NeCrediteQuUneSeuleFois()
    {
        var first = await _service.CrediterAsync(1, 25_000L, "stripe", "cs_test_123", "Dépôt par carte Stripe");
        var second = await _service.CrediterAsync(1, 25_000L, "stripe", "cs_test_123", "Dépôt par carte Stripe"); // rejeu

        Assert.True(first);
        Assert.True(second); // idempotent : pas une erreur, mais pas de double crédit
        Assert.Equal(35_000L, GetUser(1).Solde);
        Assert.Single(GetTransactions(1));
    }

    [Fact]
    public async Task Crediter_ReferencesDifferentes_CrediteLesDeux()
    {
        await _service.CrediterAsync(1, 10_000L, "flutterwave", "dep-1", "Dépôt Mobile Money");
        await _service.CrediterAsync(1, 20_000L, "flutterwave", "dep-2", "Dépôt Mobile Money");

        Assert.Equal(40_000L, GetUser(1).Solde); // 100 + 100 + 200 DH
        Assert.Equal(2, GetTransactions(1).Count);
    }

    [Fact]
    public async Task Crediter_MemeIdMaisSourcesDifferentes_CrediteLesDeux()
    {
        // « abc » chez Stripe et « abc » chez Flutterwave sont deux paiements distincts.
        await _service.CrediterAsync(1, 10_000L, "stripe", "abc", "Dépôt carte");
        await _service.CrediterAsync(1, 10_000L, "flutterwave", "abc", "Dépôt mobile");

        Assert.Equal(30_000L, GetUser(1).Solde);
        Assert.Equal(2, GetTransactions(1).Count);
    }

    [Fact]
    public async Task Crediter_MontantNonPositif_RetourneFalse()
    {
        Assert.False(await _service.CrediterAsync(1, 0L, "stripe", "cs_0", "x"));
        Assert.False(await _service.CrediterAsync(1, -5_000L, "stripe", "cs_neg", "x"));
        Assert.Equal(10_000L, GetUser(1).Solde); // inchangé
        Assert.Empty(GetTransactions(1));
    }

    [Fact]
    public async Task Crediter_ReferenceVide_RetourneFalse()
    {
        Assert.False(await _service.CrediterAsync(1, 10_000L, "stripe", "", "x"));
        Assert.Equal(10_000L, GetUser(1).Solde);
    }

    [Fact]
    public async Task Crediter_CompteInconnu_RetourneFalse()
    {
        Assert.False(await _service.CrediterAsync(404, 10_000L, "stripe", "cs_test_404", "x"));
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
