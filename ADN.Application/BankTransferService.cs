using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ADN_pay.Services
{
    // Dépôt par virement bancaire : côté client, création de la demande (référence
    // unique à mettre dans le motif du virement) ; côté admin, validation à réception
    // du virement réel (crédit idempotent via ExternalDepositService) ou rejet motivé.
    public class BankTransferService
    {
        // Limites : pas de spam de demandes, montants raisonnables (centimes, ADR-001).
        public const int MaxDemandesEnAttente = 3;
        public const long MontantMin = 50_00L;        // 50 DH
        public const long MontantMax = 50_000_00L;    // 50 000 DH (virement)

        // Garde-fous du Mobile Money manuel (phase pilote : les fonds transitent
        // par le compte mobile money personnel du fondateur, sans agrément PSP —
        // on plafonne bas tant que la société n'est pas immatriculée).
        public const long MontantMaxMobileMoney = 1_000_00L;        // 1 000 DH / dépôt
        public const long PlafondMensuelMobileMoney = 3_000_00L;    // 3 000 DH / mois / client

        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;
        private readonly ExternalDepositService _deposits;
        private readonly NotificationHistoryService _notifHist;
        private readonly ILogger<BankTransferService> _logger;

        public BankTransferService(
            IDbContextFactory<BankDbContext> factory,
            UserContext user,
            ExternalDepositService deposits,
            NotificationHistoryService notifHist,
            ILogger<BankTransferService> logger)
        {
            _factory = factory;
            _user = user;
            _deposits = deposits;
            _notifHist = notifHist;
            _logger = logger;
        }

        // ─────────────────────────── Côté client ───────────────────────────

        // tauxConversion : pour le Mobile Money, taux FCFA par DH — le montant à
        // envoyer est figé sur la demande (arrondi au FCFA supérieur).
        public async Task<(bool Success, string Message, BankTransferRequest? Demande)> CreerDemandeAsync(
            long montantCentimes, string canal = BankTransferRequest.CanalVirement, decimal? tauxConversion = null)
        {
            if (!_user.EstConnecte || _user.Profil is null)
                return (false, "Session expirée. Reconnectez-vous.", null);
            if (canal is not (BankTransferRequest.CanalVirement or BankTransferRequest.CanalMobileMoney))
                return (false, "Canal de dépôt inconnu.", null);
            if (montantCentimes < MontantMin)
                return (false, $"Montant minimum : {MontantMin.ToDh()}.", null);
            if (montantCentimes > MontantMax)
                return (false, $"Montant maximum : {MontantMax.ToDh()} par demande.", null);
            if (canal == BankTransferRequest.CanalMobileMoney && montantCentimes > MontantMaxMobileMoney)
                return (false, $"Montant maximum en Mobile Money : {MontantMaxMobileMoney.ToDh()} par dépôt (phase pilote).", null);

            await using var ctx = await _factory.CreateDbContextAsync();
            var enAttente = await ctx.BankTransferRequests
                .CountAsync(r => r.UserId == _user.Profil.Id && r.Statut == BankTransferRequest.EnAttente);
            if (enAttente >= MaxDemandesEnAttente)
                return (false, $"Vous avez déjà {enAttente} demandes en attente. Attendez leur traitement (ou annulez-en une).", null);

            if (canal == BankTransferRequest.CanalMobileMoney)
            {
                // Plafond mensuel pilote : demandes du mois civil non rejetées/annulées.
                var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var totalMois = await ctx.BankTransferRequests
                    .Where(r => r.UserId == _user.Profil.Id
                        && r.Canal == BankTransferRequest.CanalMobileMoney
                        && r.Statut != BankTransferRequest.Rejete
                        && r.Statut != BankTransferRequest.Annule
                        && r.DateCreation >= debutMois)
                    .SumAsync(r => r.MontantCentimes);
                if (totalMois + montantCentimes > PlafondMensuelMobileMoney)
                    return (false, $"Plafond Mobile Money atteint : {PlafondMensuelMobileMoney.ToDh()} par mois pendant la phase pilote " +
                        $"(déjà {totalMois.ToDh()} ce mois-ci).", null);
            }

            var demande = new BankTransferRequest
            {
                UserId = _user.Profil.Id,
                MontantCentimes = montantCentimes,
                Canal = canal,
                Reference = await GenererReferenceUniqueAsync(ctx),
            };
            if (canal == BankTransferRequest.CanalMobileMoney && tauxConversion is > 0)
            {
                // Le FCFA n'a pas de décimales : arrondi supérieur, jamais moins que l'équivalent.
                demande.MontantConverti = (long)Math.Ceiling(montantCentimes / 100m * tauxConversion.Value);
                demande.DeviseConvertie = "FCFA";
            }
            ctx.BankTransferRequests.Add(demande);
            await ctx.SaveChangesAsync();

            _logger.LogInformation("Demande de dépôt {Canal} {Reference} de {Montant} créée par {Email}",
                canal, demande.Reference, montantCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, "Demande enregistrée.", demande);
        }

        public async Task<List<BankTransferRequest>> GetMesDemandesAsync(int max = 10)
        {
            if (!_user.EstConnecte || _user.Profil is null) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.BankTransferRequests
                .Where(r => r.UserId == _user.Profil.Id)
                .OrderByDescending(r => r.DateCreation)
                .Take(max)
                .ToListAsync();
        }

        public async Task<bool> AnnulerDemandeAsync(int demandeId)
        {
            if (!_user.EstConnecte || _user.Profil is null) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var demande = await ctx.BankTransferRequests.FindAsync(demandeId);
            if (demande is null || demande.UserId != _user.Profil.Id
                || demande.Statut != BankTransferRequest.EnAttente)
                return false;

            demande.Statut = BankTransferRequest.Annule;
            demande.DateTraitement = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
            return true;
        }

        // ─────────────────────────── Côté admin ───────────────────────────

        public async Task<List<BankTransferRequest>> GetDemandesEnAttenteAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.BankTransferRequests
                .Include(r => r.User)
                .Where(r => r.Statut == BankTransferRequest.EnAttente)
                .OrderBy(r => r.DateCreation)
                .ToListAsync();
        }

        // À réception du virement réel sur le compte bancaire : crédite (une seule
        // fois, même en double-clic — référence unique) et clôt la demande.
        public async Task<bool> ValiderAsync(int demandeId)
        {
            if (!_user.Profil.IsAdmin) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var demande = await ctx.BankTransferRequests.FindAsync(demandeId);
            if (demande is null || demande.Statut != BankTransferRequest.EnAttente) return false;

            var motif = demande.Canal == BankTransferRequest.CanalMobileMoney
                ? $"Dépôt Mobile Money ({demande.Reference})"
                : $"Dépôt par virement bancaire ({demande.Reference})";
            var credite = await _deposits.CrediterAsync(
                demande.UserId, demande.MontantCentimes, "virement", demande.Reference, motif);
            if (!credite) return false;

            demande.Statut = BankTransferRequest.Valide;
            demande.DateTraitement = DateTime.UtcNow;
            demande.TraitePar = _user.Profil.Email;
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "VIREMENT_VALIDE",
                Cible = $"UserId={demande.UserId}",
                Details = $"Ref: {demande.Reference}, Montant: {demande.MontantCentimes.ToDh()}"
            });
            await ctx.SaveChangesAsync();

            _logger.LogInformation("Virement {Reference} validé par {AdminEmail}",
                demande.Reference, PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<bool> RejeterAsync(int demandeId, string motif)
        {
            if (!_user.Profil.IsAdmin || string.IsNullOrWhiteSpace(motif)) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var demande = await ctx.BankTransferRequests.FindAsync(demandeId);
            if (demande is null || demande.Statut != BankTransferRequest.EnAttente) return false;

            demande.Statut = BankTransferRequest.Rejete;
            demande.MotifRejet = motif.Trim();
            demande.DateTraitement = DateTime.UtcNow;
            demande.TraitePar = _user.Profil.Email;
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "VIREMENT_REJETE",
                Cible = $"UserId={demande.UserId}",
                Details = $"Ref: {demande.Reference}, Motif: {demande.MotifRejet}"
            });
            await ctx.SaveChangesAsync();

            await _notifHist.AddNotificationForUserAsync(demande.UserId,
                $"Votre demande de virement {demande.Reference} a été rejetée : {demande.MotifRejet}", "ERROR", "DÉPÔT");
            _logger.LogInformation("Virement {Reference} rejeté par {AdminEmail} : {Motif}",
                demande.Reference, PiiMasker.MaskEmail(_user.Profil.Email), motif);
            return true;
        }

        // Référence courte, lisible au guichet, sans caractères ambigus (pas de 0/O, 1/I…).
        private static async Task<string> GenererReferenceUniqueAsync(BankDbContext ctx)
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            for (var essai = 0; essai < 10; essai++)
            {
                var bytes = RandomNumberGenerator.GetBytes(6);
                var suffixe = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
                var reference = $"ADN-{suffixe}";
                if (!await ctx.BankTransferRequests.AnyAsync(r => r.Reference == reference))
                    return reference;
            }
            // 31^6 combinaisons : dix collisions d'affilée = problème plus grave qu'une référence.
            throw new InvalidOperationException("Impossible de générer une référence de virement unique.");
        }
    }
}
