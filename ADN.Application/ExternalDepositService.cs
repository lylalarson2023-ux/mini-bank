using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ADN_pay.Services
{
    // Crédit des dépôts venant de l'extérieur (Stripe, Flutterwave, virement bancaire).
    // Contrairement à AccountService, ce service ne dépend PAS de UserContext : il est
    // appelable depuis un webhook ou un callback serveur→serveur, sans session utilisateur.
    //
    // Idempotence : chaque dépôt porte une référence externe unique (index unique en base).
    // Rejouer le même paiement (retry du webhook, URL de succès rechargée, callback
    // dupliqué) ne peut jamais créditer deux fois.
    public class ExternalDepositService
    {
        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly NotificationHistoryService _notifHist;
        private readonly ILogger<ExternalDepositService> _logger;

        public ExternalDepositService(
            IDbContextFactory<BankDbContext> factory,
            NotificationHistoryService notifHist,
            ILogger<ExternalDepositService> logger)
        {
            _factory = factory;
            _notifHist = notifHist;
            _logger = logger;
        }

        // Crédite le compte une seule fois pour la référence donnée.
        // Retourne true si le compte est crédité OU l'a déjà été pour cette référence.
        public async Task<bool> CrediterAsync(int userId, long montantCentimes, string source, string referenceExterne, string motif)
        {
            if (montantCentimes <= 0 || string.IsNullOrWhiteSpace(referenceExterne))
                return false;

            var reference = $"{source}:{referenceExterne}";
            await using var ctx = await _factory.CreateDbContextAsync();

            if (await ctx.Transactions.AnyAsync(t => t.ReferenceExterne == reference))
            {
                _logger.LogInformation("Dépôt externe déjà crédité, rejeu ignoré — ref={Reference}", reference);
                return true;
            }

            var user = await ctx.UserProfiles.FindAsync(userId);
            if (user == null)
            {
                _logger.LogError("Dépôt externe : compte #{UserId} introuvable — ref={Reference}", userId, reference);
                return false;
            }

            user.Solde += montantCentimes;
            user.NombreTransactions++;
            ctx.Transactions.Add(new Transaction
            {
                UserId = userId,
                Montant = montantCentimes,
                Type = "DÉPÔT",
                Motif = motif,
                SoldeApres = user.Solde,
                Libelle = $"DÉPÔT {source.ToUpperInvariant()}",
                ReferenceExterne = reference,
                Date = DateTime.UtcNow
            });

            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Course entre deux rejeux simultanés (webhook + redirect) : l'index
                // unique a tranché, l'autre requête a déjà crédité. Idempotent.
                _logger.LogInformation("Dépôt externe : course perdue sur ref={Reference}, déjà crédité", reference);
                return true;
            }

            if (user.NotifDepot)
                await _notifHist.AddNotificationForUserAsync(userId,
                    $"Dépôt de {montantCentimes.ToDh()} reçu — {motif}", "SUCCESS", "DÉPÔT");

            _logger.LogInformation("Dépôt externe de {Montant} crédité sur le compte de {Email} — ref={Reference}",
                montantCentimes.ToDh(), PiiMasker.MaskEmail(user.Email), reference);
            return true;
        }
    }
}
