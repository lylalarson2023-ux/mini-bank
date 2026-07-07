using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

// Retrait par Mobile Money (canal Alex, avance de cash) : création de demande côté
// client (sans débit), validation côté admin (débite le CLIENT VISÉ, pas l'admin
// connecté, jamais avant la validation), rejet sans remboursement (rien débité).
public class MobileMoneyWithdrawalServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly UserContext _user;
    private readonly MobileMoneyWithdrawalService _service;

    public MobileMoneyWithdrawalServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BankDbContext(options);
        _db.Database.EnsureCreated();

        var factory = new TestDbContextFactory(options);
        _user = new UserContext
        {
            EstConnecte = true,
            Profil = new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test", Solde = 20_000L } // 200 DH
        };
        var notifHist = new NotificationHistoryService(factory, _user);
        _service = new MobileMoneyWithdrawalService(factory, _user, notifHist, NullLogger<MobileMoneyWithdrawalService>.Instance);

        _db.UserProfiles.Add(new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test", Solde = 20_000L }); // 200 DH
        _db.SaveChanges();
    }

    private void DevientAdmin() => _user.Profil = new UserProfile { Id = 99, Email = "admin@test.ma", IsAdmin = true };

    // Reflète le VRAI solde en base (pas une valeur figée) : après validation dans
    // une boucle, la vérification de solde à la création doit voir le solde à jour,
    // exactement comme un client rechargerait sa session entre deux demandes.
    private void RedevientClient()
    {
        _db.ChangeTracker.Clear();
        var solde = _db.UserProfiles.Find(1)!.Solde;
        _user.Profil = new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test", Solde = solde };
    }
    private UserProfile GetUser(int id) { _db.ChangeTracker.Clear(); return _db.UserProfiles.Find(id)!; }
    private MobileMoneyWithdrawalRequest GetDemande(int id) { _db.ChangeTracker.Clear(); return _db.MobileMoneyWithdrawalRequests.Find(id)!; }

    // ─────────────────────────── Création (client) ───────────────────────────

    [Fact]
    public async Task Creer_DemandeValide_GenereReferenceEtStatutEnAttenteSansDebit()
    {
        var (ok, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche"); // 100 DH

        Assert.True(ok);
        Assert.NotNull(demande);
        Assert.Matches(@"^ADN-[A-Z2-9]{6}$", demande!.Reference);
        Assert.Equal(MobileMoneyWithdrawalRequest.EnAttente, demande.Statut);
        Assert.Equal(20_000L, GetUser(1).Solde); // pas de débit avant validation admin
    }

    [Fact]
    public async Task Creer_SansNumeroBeneficiaire_Refuse()
    {
        var (ok, _, _) = await _service.CreerDemandeAsync(10_000L, "  ", "Proche");
        Assert.False(ok);
    }

    [Fact]
    public async Task Creer_MontantSousLeMinimum_Refuse()
    {
        var (ok, _, _) = await _service.CreerDemandeAsync(MobileMoneyWithdrawalService.MontantMin - 1, "074000000", "Proche");
        Assert.False(ok);
    }

    [Fact]
    public async Task Creer_MontantAuDessusDuMaximumPilote_Refuse()
    {
        var (ok, message, _) = await _service.CreerDemandeAsync(MobileMoneyWithdrawalService.MontantMax + 1, "074000000", "Proche");
        Assert.False(ok);
        Assert.Contains("pilote", message);
    }

    [Fact]
    public async Task Creer_SoldeInsuffisant_Refuse()
    {
        var (ok, message, _) = await _service.CreerDemandeAsync(50_000L, "074000000", "Proche"); // solde = 200 DH
        Assert.False(ok);
        Assert.Contains("insuffisant", message);
    }

    [Fact]
    public async Task Creer_FigeLaConversionEnFcfa()
    {
        var (ok, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche", tauxConversion: 60m); // 100 DH

        Assert.True(ok);
        Assert.Equal(6_000L, demande!.MontantAEnvoyer); // 100 × 60 = 6 000 FCFA
        Assert.Equal("FCFA", demande.DeviseEnvoi);
    }

    [Fact]
    public async Task Creer_TropDeDemandesEnAttente_Refuse()
    {
        for (var i = 0; i < MobileMoneyWithdrawalService.MaxDemandesEnAttente; i++)
            Assert.True((await _service.CreerDemandeAsync(5_000L, "074000000", "Proche")).Success); // 50 DH = MontantMin

        var (ok, message, _) = await _service.CreerDemandeAsync(5_000L, "074000000", "Proche");

        Assert.False(ok);
        Assert.Contains("en attente", message);
    }

    [Fact]
    public async Task Creer_PlafondMensuel_Refuse()
    {
        // Solde suffisant pour enchaîner 3 retraits de 1 000 DH (max pilote) = plafond
        // mensuel (3 000 DH) atteint ; on valide au fur et à mesure pour ne pas buter
        // sur MaxDemandesEnAttente, et on relit le solde réel entre deux demandes.
        var u = _db.UserProfiles.Find(1)!;
        u.Solde = 500_000L; // 5 000 DH
        _db.SaveChanges();
        RedevientClient();

        for (var i = 0; i < 3; i++)
        {
            var (ok, _, demande) = await _service.CreerDemandeAsync(
                MobileMoneyWithdrawalService.MontantMax, "074000000", "Proche");
            Assert.True(ok);
            DevientAdmin();
            Assert.True(await _service.ValiderAsync(demande!.Id));
            RedevientClient();
        }

        var (refus, message, _) = await _service.CreerDemandeAsync(5_000L, "074000000", "Proche"); // 50 DH

        Assert.False(refus);
        Assert.Contains("Plafond", message);
    }

    [Fact]
    public async Task Annuler_PropreDemandeEnAttente_PasseAnnulee()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche");

        Assert.True(await _service.AnnulerDemandeAsync(demande!.Id));
        Assert.Equal(MobileMoneyWithdrawalRequest.Annule, GetDemande(demande.Id).Statut);
    }

    // ─────────────────────────── Validation (admin) ───────────────────────────

    [Fact]
    public async Task Valider_DemandeEnAttente_DebiteLeClientVise_PasLAdmin()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche"); // 100 DH
        DevientAdmin();

        var ok = await _service.ValiderAsync(demande!.Id);

        Assert.True(ok);
        Assert.Equal(10_000L, GetUser(1).Solde); // 200 - 100 DH : le CLIENT est débité
        var apres = GetDemande(demande.Id);
        Assert.Equal(MobileMoneyWithdrawalRequest.Valide, apres.Statut);
        Assert.Equal("admin@test.ma", apres.TraitePar);
        var tx = Assert.Single(_db.Transactions.Where(t => t.UserId == 1).ToList());
        Assert.Equal("RETRAIT", tx.Type);
        Assert.Equal(10_000L, tx.Montant);
    }

    [Fact]
    public async Task Valider_DoubleClic_NeDebitQuUneSeuleFois()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche");
        DevientAdmin();

        var first = await _service.ValiderAsync(demande!.Id);
        var second = await _service.ValiderAsync(demande.Id); // déjà VALIDE

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(10_000L, GetUser(1).Solde); // débité une seule fois
    }

    [Fact]
    public async Task Valider_SoldeDevenuInsuffisantEntreTemps_Refuse()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche"); // 100 DH
        // Le solde chute sous le montant demandé avant que l'admin ne valide (ex. une
        // autre opération entre-temps) — la revérification sur la valeur DB doit refuser.
        var u = _db.UserProfiles.Find(1)!;
        u.Solde = 5_000L;
        _db.SaveChanges();
        DevientAdmin();

        var ok = await _service.ValiderAsync(demande!.Id);

        Assert.False(ok);
        Assert.Equal(5_000L, GetUser(1).Solde); // inchangé
        Assert.Equal(MobileMoneyWithdrawalRequest.EnAttente, GetDemande(demande.Id).Statut); // reste en attente
    }

    [Fact]
    public async Task Valider_NonAdmin_Refuse()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche");

        Assert.False(await _service.ValiderAsync(demande!.Id)); // toujours client
        Assert.Equal(20_000L, GetUser(1).Solde);
    }

    [Fact]
    public async Task Rejeter_DemandeEnAttente_PasDeDebitEtMotifConserve()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche");
        DevientAdmin();

        var ok = await _service.RejeterAsync(demande!.Id, "Bénéficiaire injoignable");

        Assert.True(ok);
        Assert.Equal(20_000L, GetUser(1).Solde); // aucun débit (rien n'avait été prélevé)
        var apres = GetDemande(demande.Id);
        Assert.Equal(MobileMoneyWithdrawalRequest.Rejete, apres.Statut);
        Assert.Equal("Bénéficiaire injoignable", apres.MotifRejet);
    }

    [Fact]
    public async Task Rejeter_SansMotif_Refuse()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(10_000L, "074000000", "Proche");
        DevientAdmin();

        Assert.False(await _service.RejeterAsync(demande!.Id, "  "));
        Assert.Equal(MobileMoneyWithdrawalRequest.EnAttente, GetDemande(demande.Id).Statut);
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
