using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

public class AdminServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly IDbContextFactory<BankDbContext> _factory;
    private readonly AdminService _service;
    private readonly UserContext _user;

    public AdminServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BankDbContext(options);
        _db.Database.EnsureCreated();

        _factory = new TestDbContextFactory(options);

        // Contexte admin (lecture seule : IsAdmin + Email pour les logs)
        _user = new UserContext
        {
            EstConnecte = true,
            Profil = new UserProfile { Id = 99, Email = "admin@test.ma", Nom = "Root", Prenom = "Admin", IsAdmin = true }
        };
        var notifHist = new NotificationHistoryService(_factory, _user);
        _service = new AdminService(_factory, _user, NullLogger<AdminService>.Instance, notifHist);

        // Seed : 1 client standard — montants en centimes (ADR-001)
        _db.UserProfiles.Add(new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test", Solde = 10_000L }); // 100 DH
        _db.SaveChanges();
    }

    private UserProfile GetUser(int id) { _db.ChangeTracker.Clear(); return _db.UserProfiles.Find(id)!; }
    private CreditRequest GetCredit(int id) { _db.ChangeTracker.Clear(); return _db.CreditRequests.Find(id)!; }
    private int TransactionCount(int userId) { _db.ChangeTracker.Clear(); return _db.Transactions.Count(t => t.UserId == userId); }

    // ─────────────────────────── ApprouverCredit ───────────────────────────

    [Fact]
    public async Task ApprouverCredit_DemandeEnAttente_CrediteSoldeEtDette()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.PendingCreditRequest = true;
        u.PendingCreditAmount = 20_000L; // 200 DH
        _db.CreditRequests.Add(new CreditRequest { Id = 1, UserId = 1, Montant = 20_000L, Statut = "EN_ATTENTE" });
        _db.SaveChanges();

        var result = await _service.ApprouverCredit(1);

        Assert.True(result);
        var after = GetUser(1);
        Assert.Equal(30_000L, after.Solde);              // 100 + 200 = 300 DH
        Assert.Equal(20_000L, after.Dette);              // dette +200 DH
        Assert.False(after.PendingCreditRequest);
        Assert.Equal("APPROUVE", GetCredit(1).Statut);
    }

    [Fact]
    public async Task ApprouverCredit_DoubleClic_NeCrediteQuUneSeuleFois()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.PendingCreditRequest = true;
        u.PendingCreditAmount = 20_000L;
        _db.CreditRequests.Add(new CreditRequest { Id = 1, UserId = 1, Montant = 20_000L, Statut = "EN_ATTENTE" });
        _db.SaveChanges();

        var first = await _service.ApprouverCredit(1);
        var second = await _service.ApprouverCredit(1); // 2e clic : la demande est déjà APPROUVE

        Assert.True(first);
        Assert.False(second); // garde : plus rien à approuver
        Assert.Equal(30_000L, GetUser(1).Solde); // crédité une seule fois (et non 50 000)
        Assert.Equal(20_000L, GetUser(1).Dette);
    }

    [Fact]
    public async Task ApprouverCredit_SansDemandeNiMontantForce_RetourneFalse()
    {
        var result = await _service.ApprouverCredit(1); // aucun PendingCreditRequest, pas de demande

        Assert.False(result);
        Assert.Equal(10_000L, GetUser(1).Solde); // inchangé
        Assert.Equal(0L, GetUser(1).Dette);
    }

    [Fact]
    public async Task ApprouverCredit_MontantForce_CrediteMemeSansDemande()
    {
        var result = await _service.ApprouverCredit(1, 30_000L); // octroi manuel 300 DH

        Assert.True(result);
        var after = GetUser(1);
        Assert.Equal(40_000L, after.Solde); // 100 + 300 = 400 DH
        Assert.Equal(30_000L, after.Dette);
    }

    [Fact]
    public async Task ApprouverCredit_NonAdmin_RetourneFalse()
    {
        _user.Profil = new UserProfile { Id = 1, Email = "client@test.ma", IsAdmin = false };

        var result = await _service.ApprouverCredit(1, 30_000L);

        Assert.False(result);
        Assert.Equal(10_000L, GetUser(1).Solde); // inchangé
    }

    // ─────────────────────────── ResetPasswordAsync ───────────────────────────

    [Fact]
    public async Task ResetPasswordAsync_Admin_RemplaceLeHashParLeNouveauMotDePasse()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword("AncienMdp1!");
        _db.SaveChanges();

        var ok = await _service.ResetPasswordAsync(1, "NouveauMdp1!");

        Assert.True(ok);
        var after = GetUser(1);
        Assert.True(BCrypt.Net.BCrypt.Verify("NouveauMdp1!", after.MotDePasseHash));  // le nouveau marche
        Assert.False(BCrypt.Net.BCrypt.Verify("AncienMdp1!", after.MotDePasseHash));  // l'ancien ne marche plus
    }

    [Fact]
    public async Task ResetPasswordAsync_NonAdmin_RefuseEtLaisseLeHashIntact()
    {
        var u = _db.UserProfiles.Find(1)!;
        u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword("AncienMdp1!");
        _db.SaveChanges();
        _user.Profil = new UserProfile { Id = 1, Email = "client@test.ma", IsAdmin = false };

        var ok = await _service.ResetPasswordAsync(1, "NouveauMdp1!");

        Assert.False(ok);
        Assert.True(BCrypt.Net.BCrypt.Verify("AncienMdp1!", GetUser(1).MotDePasseHash)); // inchangé
    }

    [Fact]
    public async Task ResetPasswordAsync_UtilisateurIntrouvable_RetourneFalse()
    {
        Assert.False(await _service.ResetPasswordAsync(404, "NouveauMdp1!"));
    }

    // ─────────────────────────── AdminDepot ───────────────────────────

    [Fact]
    public async Task AdminDepot_AjouteSoldeEtCreeTransaction()
    {
        var result = await _service.AdminDepot(1, 30_000L); // 300 DH

        Assert.True(result);
        Assert.Equal(40_000L, GetUser(1).Solde); // 100 + 300 = 400 DH
        Assert.Equal(1, TransactionCount(1));
    }

    [Fact]
    public async Task AdminDepot_DoubleClic_NeCrediteQuUneSeuleFois()
    {
        var first = await _service.AdminDepot(1, 30_000L);
        var second = await _service.AdminDepot(1, 30_000L); // 2e clic identique : doublon

        Assert.True(first);
        Assert.True(second); // idempotent (succès sans recréditer)
        Assert.Equal(40_000L, GetUser(1).Solde); // crédité une seule fois
        Assert.Equal(1, TransactionCount(1)); // une seule transaction
    }

    [Fact]
    public async Task AdminDepot_MontantsDifferents_CrediteLesDeux()
    {
        await _service.AdminDepot(1, 10_000L); // 100 DH
        await _service.AdminDepot(1, 20_000L); // 200 DH (montant différent → pas un doublon)

        Assert.Equal(40_000L, GetUser(1).Solde); // 100 + 100 + 200 = 400 DH
        Assert.Equal(2, TransactionCount(1));
    }

    [Fact]
    public async Task AdminDepot_MontantNonPositif_RetourneFalse()
    {
        var result = await _service.AdminDepot(1, 0L);

        Assert.False(result);
        Assert.Equal(10_000L, GetUser(1).Solde); // inchangé
        Assert.Equal(0, TransactionCount(1));
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
