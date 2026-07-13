using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADN_pay.Tests;

// Dépôt par virement bancaire : création de demande côté client (référence unique,
// limites), validation/rejet côté admin (crédit idempotent, pas de double crédit).
public class BankTransferServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BankDbContext _db;
    private readonly UserContext _user;
    private readonly BankTransferService _service;

    public BankTransferServiceTests()
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
            Profil = new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test" }
        };
        var notifHist = new NotificationHistoryService(factory, _user);
        var email = new LogEmailSender(NullLogger<LogEmailSender>.Instance);
        var deposits = new ExternalDepositService(factory, notifHist, email, NullLogger<ExternalDepositService>.Instance);
        _service = new BankTransferService(factory, _user, deposits, notifHist, NullLogger<BankTransferService>.Instance);

        _db.UserProfiles.Add(new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test", Solde = 10_000L }); // 100 DH
        _db.SaveChanges();
    }

    private void DevientAdmin() => _user.Profil = new UserProfile { Id = 99, Email = "admin@test.ma", IsAdmin = true };
    private UserProfile GetUser(int id) { _db.ChangeTracker.Clear(); return _db.UserProfiles.Find(id)!; }
    private BankTransferRequest GetDemande(int id) { _db.ChangeTracker.Clear(); return _db.BankTransferRequests.Find(id)!; }

    // ─────────────────────────── Création (client) ───────────────────────────

    [Fact]
    public async Task Creer_DemandeValide_GenereReferenceEtStatutEnAttente()
    {
        var (ok, _, demande) = await _service.CreerDemandeAsync(20_000L); // 200 DH

        Assert.True(ok);
        Assert.NotNull(demande);
        Assert.Matches(@"^ADN-[A-Z2-9]{6}$", demande!.Reference);
        Assert.Equal(BankTransferRequest.EnAttente, demande.Statut);
        Assert.Equal(10_000L, GetUser(1).Solde); // pas de crédit avant validation admin
    }

    [Fact]
    public async Task Creer_MontantSousLeMinimum_Refuse()
    {
        var (ok, _, _) = await _service.CreerDemandeAsync(BankTransferService.MontantMin - 1);
        Assert.False(ok);
    }

    [Fact]
    public async Task Creer_MontantAuDessusDuMaximum_Refuse()
    {
        var (ok, _, _) = await _service.CreerDemandeAsync(BankTransferService.MontantMax + 1);
        Assert.False(ok);
    }

    [Fact]
    public async Task Creer_TropDeDemandesEnAttente_Refuse()
    {
        for (var i = 0; i < BankTransferService.MaxDemandesEnAttente; i++)
            Assert.True((await _service.CreerDemandeAsync(10_000L)).Success);

        var (ok, message, _) = await _service.CreerDemandeAsync(10_000L);

        Assert.False(ok);
        Assert.Contains("en attente", message);
    }

    // ─────────────────────────── Mobile Money (pilote) ───────────────────────────

    [Fact]
    public async Task CreerMobileMoney_FigeLaConversionEnFcfa()
    {
        // 140,40 DH voulus, taux 0,0156 DH/FCFA, marge 10% déduite du crédité :
        // taux effectif 0,01404 DH/FCFA → 140,40 / 0,01404 = 10 000 FCFA à envoyer.
        var (ok, _, demande) = await _service.CreerDemandeAsync(
            14_040L, BankTransferRequest.CanalMobileMoney, tauxDhParFcfa: 0.0156m, margePct: 0.10m);

        Assert.True(ok);
        Assert.Equal(BankTransferRequest.CanalMobileMoney, demande!.Canal);
        Assert.Equal(10_000L, demande.MontantConverti);
        Assert.Equal("FCFA", demande.DeviseConvertie);
    }

    [Fact]
    public async Task CreerMobileMoney_ArrondiAuFcfaSuperieur()
    {
        // 140,41 DH (1 centime de plus) → 10 000,71... → arrondi à 10 001 FCFA
        // (jamais moins que l'équivalent exact, protège la marge côté demande).
        var (_, _, demande) = await _service.CreerDemandeAsync(
            14_041L, BankTransferRequest.CanalMobileMoney, tauxDhParFcfa: 0.0156m, margePct: 0.10m);

        Assert.Equal(10_001L, demande!.MontantConverti);
    }

    [Fact]
    public async Task CreerMobileMoney_StockeLesFraisDeChange()
    {
        // 140,40 DH crédités, marge 10% déduite → 15,60 DH de frais de change figés.
        var (ok, _, demande) = await _service.CreerDemandeAsync(
            14_040L, BankTransferRequest.CanalMobileMoney, tauxDhParFcfa: 0.0156m, margePct: 0.10m);

        Assert.True(ok);
        Assert.Equal(1_560L, demande!.FraisCentimes);
    }

    [Fact]
    public async Task CreerVirement_SansChange_FraisNuls()
    {
        var (ok, _, demande) = await _service.CreerDemandeAsync(20_000L); // virement bancaire

        Assert.True(ok);
        Assert.Equal(0L, demande!.FraisCentimes);
    }

    [Fact]
    public async Task Valider_DepotMobileMoney_ReporteLesFraisDansLaTransaction()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(
            14_040L, BankTransferRequest.CanalMobileMoney, tauxDhParFcfa: 0.0156m, margePct: 0.10m);
        DevientAdmin();

        Assert.True(await _service.ValiderAsync(demande!.Id));

        var tx = Assert.Single(_db.Transactions.Where(t => t.UserId == 1).ToList());
        Assert.Equal(14_040L, tx.Montant);  // crédité en plein (le proche a payé le frais)
        Assert.Equal(1_560L, tx.Frais);     // marge consignée pour la transparence
    }

    [Fact]
    public async Task CreerMobileMoney_AuDessusDuPlafondParDepot_Refuse()
    {
        var (ok, message, _) = await _service.CreerDemandeAsync(
            BankTransferService.MontantMaxMobileMoney + 1, BankTransferRequest.CanalMobileMoney, 60m);

        Assert.False(ok);
        Assert.Contains("pilote", message);
    }

    [Fact]
    public async Task CreerMobileMoney_PlafondMensuel_Refuse()
    {
        // 3 dépôts de 1 000 DH = plafond mensuel (3 000 DH) atteint.
        for (var i = 0; i < 3; i++)
        {
            var (ok, _, demande) = await _service.CreerDemandeAsync(
                BankTransferService.MontantMaxMobileMoney, BankTransferRequest.CanalMobileMoney, 60m);
            Assert.True(ok);
            // On valide au fur et à mesure pour ne pas buter sur MaxDemandesEnAttente :
            // le plafond mensuel doit compter les demandes VALIDÉES aussi.
            DevientAdmin();
            Assert.True(await _service.ValiderAsync(demande!.Id));
            _user.Profil = new UserProfile { Id = 1, Email = "client@test.ma", Nom = "Client", Prenom = "Test" };
        }

        var (refus, message, _) = await _service.CreerDemandeAsync(
            5_000L, BankTransferRequest.CanalMobileMoney, 60m); // 50 DH de plus

        Assert.False(refus);
        Assert.Contains("Plafond", message);
    }

    [Fact]
    public async Task CreerMobileMoney_LePlafondNeTouchePasLeVirement()
    {
        // Le virement garde son plafond de 50 000 DH, indépendant du pilote Mobile Money.
        var (ok, _, demande) = await _service.CreerDemandeAsync(10_000_00L); // 10 000 DH

        Assert.True(ok);
        Assert.Equal(BankTransferRequest.CanalVirement, demande!.Canal);
        Assert.Null(demande.MontantConverti);
    }

    [Fact]
    public async Task Annuler_PropreDemandeEnAttente_PasseAnnulee()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(20_000L);

        Assert.True(await _service.AnnulerDemandeAsync(demande!.Id));
        Assert.Equal(BankTransferRequest.Annule, GetDemande(demande.Id).Statut);
    }

    // ─────────────────────────── Validation (admin) ───────────────────────────

    [Fact]
    public async Task Valider_DemandeEnAttente_CrediteEtCloture()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(20_000L);
        DevientAdmin();

        var ok = await _service.ValiderAsync(demande!.Id);

        Assert.True(ok);
        Assert.Equal(30_000L, GetUser(1).Solde); // 100 + 200 DH
        var apres = GetDemande(demande.Id);
        Assert.Equal(BankTransferRequest.Valide, apres.Statut);
        Assert.Equal("admin@test.ma", apres.TraitePar);
        var tx = Assert.Single(_db.Transactions.Where(t => t.UserId == 1).ToList());
        Assert.Equal($"virement:{demande.Reference}", tx.ReferenceExterne);
    }

    [Fact]
    public async Task Valider_DoubleClic_NeCrediteQuUneSeuleFois()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(20_000L);
        DevientAdmin();

        var first = await _service.ValiderAsync(demande!.Id);
        var second = await _service.ValiderAsync(demande.Id); // déjà VALIDE

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(30_000L, GetUser(1).Solde); // crédité une seule fois
    }

    [Fact]
    public async Task Valider_NonAdmin_Refuse()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(20_000L);

        Assert.False(await _service.ValiderAsync(demande!.Id)); // toujours client
        Assert.Equal(10_000L, GetUser(1).Solde);
    }

    [Fact]
    public async Task Rejeter_DemandeEnAttente_PasDeCreditEtMotifConserve()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(20_000L);
        DevientAdmin();

        var ok = await _service.RejeterAsync(demande!.Id, "Virement jamais reçu");

        Assert.True(ok);
        Assert.Equal(10_000L, GetUser(1).Solde); // aucun crédit
        var apres = GetDemande(demande.Id);
        Assert.Equal(BankTransferRequest.Rejete, apres.Statut);
        Assert.Equal("Virement jamais reçu", apres.MotifRejet);
    }

    [Fact]
    public async Task Rejeter_SansMotif_Refuse()
    {
        var (_, _, demande) = await _service.CreerDemandeAsync(20_000L);
        DevientAdmin();

        Assert.False(await _service.RejeterAsync(demande!.Id, "  "));
        Assert.Equal(BankTransferRequest.EnAttente, GetDemande(demande.Id).Statut);
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
