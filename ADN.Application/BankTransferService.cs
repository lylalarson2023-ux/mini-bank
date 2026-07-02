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
        public const long MontantMax = 50_000_00L;    // 50 000 DH

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

        public async Task<(bool Success, string Message, BankTransferRequest? Demande)> CreerDemandeAsync(long montantCentimes)
        {
            if (!_user.EstConnecte || _user.Profil is null)
                return (false, "Session expirée. Reconnectez-vous.", null);
            if (montantCentimes < MontantMin)
                return (false, $"Montant minimum : {MontantMin.ToDh()}.", null);
            if (montantCentimes > MontantMax)
                return (false, $"Montant maximum : {MontantMax.ToDh()} par demande.", null);

            await using var ctx = await _factory.CreateDbContextAsync();
            var enAttente = await ctx.BankTransferRequests
                .CountAsync(r => r.UserId == _user.Profil.Id && r.Statut == BankTransferRequest.EnAttente);
            if (enAttente >= MaxDemandesEnAttente)
                return (false, $"Vous avez déjà {enAttente} demandes en attente. Attendez leur traitement (ou annulez-en une).", null);

            var demande = new BankTransferRequest
            {
                UserId = _user.Profil.Id,
                MontantCentimes = montantCentimes,
                Reference = await GenererReferenceUniqueAsync(ctx),
            };
            ctx.BankTransferRequests.Add(demande);
            await ctx.SaveChangesAsync();

            _logger.LogInformation("Demande de virement {Reference} de {Montant} créée par {Email}",
                demande.Reference, montantCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email));
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

            var credite = await _deposits.CrediterAsync(
                demande.UserId, demande.MontantCentimes, "virement", demande.Reference,
                $"Dépôt par virement bancaire ({demande.Reference})");
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
