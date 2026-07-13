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
        private readonly IEmailSender _email;
        private readonly ILogger<ExternalDepositService> _logger;

        public ExternalDepositService(
            IDbContextFactory<BankDbContext> factory,
            NotificationHistoryService notifHist,
            IEmailSender email,
            ILogger<ExternalDepositService> logger)
        {
            _factory = factory;
            _notifHist = notifHist;
            _email = email;
            _logger = logger;
        }

        // Crédite le compte une seule fois pour la référence donnée.
        // Retourne true si le compte est crédité OU l'a déjà été pour cette référence.
        // fraisCentimes : marge de change consignée (informative) dans Transaction.Frais
        // — 0 pour les dépôts sans change (Stripe, virement bancaire).
        public async Task<bool> CrediterAsync(int userId, long montantCentimes, string source, string referenceExterne, string motif, long fraisCentimes = 0)
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
                Frais = fraisCentimes,
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
            {
                await _notifHist.AddNotificationForUserAsync(userId,
                    $"Dépôt de {montantCentimes.ToDh()} reçu — {motif}", "SUCCESS", "DÉPÔT");
                // E-mail de marque (best-effort : n'affecte jamais le crédit déjà commis).
                try
                {
                    var prenom = EmailTemplate.Escape(user.Prenom);
                    var html = EmailTemplate.Wrap(
                        "Dépôt crédité sur votre compte",
                        EmailTemplate.Paragraphe($"Bonjour{(string.IsNullOrWhiteSpace(prenom) ? "" : " " + prenom)},")
                        + EmailTemplate.Paragraphe($"Votre compte ADN_pay a été crédité de <strong>{montantCentimes.ToDh()}</strong>.")
                        + EmailTemplate.Paragraphe($"Motif : {EmailTemplate.Escape(motif)}")
                        + EmailTemplate.Note($"Nouveau solde : {user.Solde.ToDh()}."),
                        preheader: $"Dépôt de {montantCentimes.ToDh()} crédité sur votre compte ADN_pay.");
                    await _email.SendAsync(user.Email, "ADN_pay — Dépôt crédité", html,
                        $"Votre compte ADN_pay a été crédité de {montantCentimes.ToDh()} ({motif}).");
                }
                catch (Exception exMail) { _logger.LogWarning(exMail, "E-mail de dépôt non envoyé (non bloquant)."); }
            }

            _logger.LogInformation("Dépôt externe de {Montant} crédité sur le compte de {Email} — ref={Reference}",
                montantCentimes.ToDh(), PiiMasker.MaskEmail(user.Email), reference);
            return true;
        }
    }
}
