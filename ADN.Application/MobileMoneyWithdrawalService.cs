using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ADN_pay.Services
{
    // Retrait par Mobile Money (canal Alex, avance de cash) : côté client, création
    // d'une demande (montant + destinataire) ; côté admin, validation une fois
    // l'envoi réel effectué — c'est cette validation qui débite le solde, jamais la
    // création (le client ne doit pas être débité tant qu'il n'a pas reçu l'argent).
    // Symétrique de BankTransferService (dépôt), sens inversé.
    public class MobileMoneyWithdrawalService
    {
        // Mêmes garde-fous pilote que le dépôt Mobile Money (Alex avance les fonds
        // de sa poche ; le taux/plafond du compte courant conjoint n'étant pas
        // finalisé, on plafonne bas pour limiter son exposition).
        public const int MaxDemandesEnAttente = 3;
        public const long MontantMin = 50_00L;               // 50 DH
        public const long MontantMax = 1_000_00L;            // 1 000 DH / retrait (pilote)
        public const long PlafondMensuel = 3_000_00L;        // 3 000 DH / mois / client

        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;
        private readonly NotificationHistoryService _notifHist;
        private readonly ILogger<MobileMoneyWithdrawalService> _logger;

        public MobileMoneyWithdrawalService(
            IDbContextFactory<BankDbContext> factory,
            UserContext user,
            NotificationHistoryService notifHist,
            ILogger<MobileMoneyWithdrawalService> logger)
        {
            _factory = factory;
            _user = user;
            _notifHist = notifHist;
            _logger = logger;
        }

        // ─────────────────────────── Côté client ───────────────────────────

        // tauxConversion : FCFA par DH — le montant à envoyer par Alex est figé sur
        // la demande (arrondi au FCFA supérieur, jamais moins que l'équivalent).
        public async Task<(bool Success, string Message, MobileMoneyWithdrawalRequest? Demande)> CreerDemandeAsync(
            long montantCentimes, string numeroBeneficiaire, string nomBeneficiaire, decimal? tauxConversion = null)
        {
            if (!_user.EstConnecte || _user.Profil is null)
                return (false, "Session expirée. Reconnectez-vous.", null);
            if (string.IsNullOrWhiteSpace(numeroBeneficiaire))
                return (false, "Numéro Mobile Money du bénéficiaire requis.", null);
            if (montantCentimes < MontantMin)
                return (false, $"Montant minimum : {MontantMin.ToDh()}.", null);
            if (montantCentimes > MontantMax)
                return (false, $"Montant maximum : {MontantMax.ToDh()} par retrait (phase pilote).", null);

            await using var ctx = await _factory.CreateDbContextAsync();
            var demandesEnAttente = await ctx.MobileMoneyWithdrawalRequests
                .Where(r => r.UserId == _user.Profil.Id && r.Statut == MobileMoneyWithdrawalRequest.EnAttente)
                .ToListAsync();
            if (demandesEnAttente.Count >= MaxDemandesEnAttente)
                return (false, $"Vous avez déjà {demandesEnAttente.Count} demandes en attente. Attendez leur traitement (ou annulez-en une).", null);

            // Le solde n'étant débité qu'à la validation, on tient compte des demandes
            // déjà en attente pour éviter d'accepter une demande vouée à être rejetée
            // faute de fonds au moment du traitement par Alex.
            var sommeEnAttente = demandesEnAttente.Sum(r => r.MontantCentimes);
            if (sommeEnAttente + montantCentimes > _user.Profil.Solde)
                return (false, "Solde insuffisant compte tenu de vos demandes de retrait en attente.", null);

            // Plafond mensuel pilote : demandes du mois civil non rejetées/annulées
            // (en attente + validées comptent — protège l'exposition d'Alex).
            var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var totalMois = await ctx.MobileMoneyWithdrawalRequests
                .Where(r => r.UserId == _user.Profil.Id
                    && r.Statut != MobileMoneyWithdrawalRequest.Rejete
                    && r.Statut != MobileMoneyWithdrawalRequest.Annule
                    && r.DateCreation >= debutMois)
                .SumAsync(r => r.MontantCentimes);
            if (totalMois + montantCentimes > PlafondMensuel)
                return (false, $"Plafond Mobile Money atteint : {PlafondMensuel.ToDh()} par mois pendant la phase pilote " +
                    $"(déjà {totalMois.ToDh()} ce mois-ci).", null);

            var demande = new MobileMoneyWithdrawalRequest
            {
                UserId = _user.Profil.Id,
                MontantCentimes = montantCentimes,
                NumeroBeneficiaire = numeroBeneficiaire.Trim(),
                NomBeneficiaire = nomBeneficiaire?.Trim() ?? "",
                Reference = await GenererReferenceUniqueAsync(ctx),
            };
            if (tauxConversion is > 0)
            {
                demande.MontantAEnvoyer = (long)Math.Ceiling(montantCentimes / 100m * tauxConversion.Value);
                demande.DeviseEnvoi = "FCFA";
            }
            ctx.MobileMoneyWithdrawalRequests.Add(demande);
            await ctx.SaveChangesAsync();

            _logger.LogInformation("Demande de retrait Mobile Money {Reference} de {Montant} créée par {Email}",
                demande.Reference, montantCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, "Demande enregistrée.", demande);
        }

        public async Task<List<MobileMoneyWithdrawalRequest>> GetMesDemandesAsync(int max = 10)
        {
            if (!_user.EstConnecte || _user.Profil is null) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.MobileMoneyWithdrawalRequests
                .Where(r => r.UserId == _user.Profil.Id)
                .OrderByDescending(r => r.DateCreation)
                .Take(max)
                .ToListAsync();
        }

        public async Task<bool> AnnulerDemandeAsync(int demandeId)
        {
            if (!_user.EstConnecte || _user.Profil is null) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var demande = await ctx.MobileMoneyWithdrawalRequests.FindAsync(demandeId);
            if (demande is null || demande.UserId != _user.Profil.Id
                || demande.Statut != MobileMoneyWithdrawalRequest.EnAttente)
                return false;

            demande.Statut = MobileMoneyWithdrawalRequest.Annule;
            demande.DateTraitement = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
            return true;
        }

        // ─────────────────────────── Côté admin ───────────────────────────

        public async Task<List<MobileMoneyWithdrawalRequest>> GetDemandesEnAttenteAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.MobileMoneyWithdrawalRequests
                .Include(r => r.User)
                .Where(r => r.Statut == MobileMoneyWithdrawalRequest.EnAttente)
                .OrderBy(r => r.DateCreation)
                .ToListAsync();
        }

        // À envoi Mobile Money réel effectué (Alex a payé le bénéficiaire) : débite
        // le solde du CLIENT VISÉ (userId de la demande, jamais l'admin connecté) et
        // clôt la demande. Le statut EN_ATTENTE agit comme verrou anti-double-clic.
        public async Task<bool> ValiderAsync(int demandeId)
        {
            if (!_user.Profil.IsAdmin) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var demande = await ctx.MobileMoneyWithdrawalRequests.FindAsync(demandeId);
            if (demande is null || demande.Statut != MobileMoneyWithdrawalRequest.EnAttente) return false;

            var u = await ctx.UserProfiles.FindAsync(demande.UserId);
            if (u is null) return false;
            if (u.Solde < demande.MontantCentimes)
            {
                _logger.LogWarning("Validation retrait {Reference} refusée : solde insuffisant (solde={Solde}, montant={Montant})",
                    demande.Reference, u.Solde.ToDh(), demande.MontantCentimes.ToDh());
                return false;
            }

            u.Solde -= demande.MontantCentimes;
            ctx.Transactions.Add(new Transaction
            {
                UserId = demande.UserId,
                Montant = demande.MontantCentimes,
                Type = "RETRAIT",
                Motif = $"Retrait Mobile Money ({demande.Reference})",
                SoldeApres = u.Solde,
                Libelle = "RETRAIT MOBILE MONEY",
                Date = DateTime.UtcNow
            });

            demande.Statut = MobileMoneyWithdrawalRequest.Valide;
            demande.DateTraitement = DateTime.UtcNow;
            demande.TraitePar = _user.Profil.Email;
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "RETRAIT_MOBILEMONEY_VALIDE",
                Cible = $"UserId={demande.UserId}",
                Details = $"Ref: {demande.Reference}, Montant: {demande.MontantCentimes.ToDh()}, Bénéficiaire: {demande.NumeroBeneficiaire}"
            });
            await ctx.SaveChangesAsync();

            await _notifHist.AddNotificationForUserAsync(demande.UserId,
                $"Votre retrait Mobile Money {demande.Reference} a été effectué ({demande.MontantCentimes.ToDh()})", "SUCCESS", "RETRAIT");
            _logger.LogInformation("Retrait Mobile Money {Reference} validé par {AdminEmail}",
                demande.Reference, PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<bool> RejeterAsync(int demandeId, string motif)
        {
            if (!_user.Profil.IsAdmin || string.IsNullOrWhiteSpace(motif)) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var demande = await ctx.MobileMoneyWithdrawalRequests.FindAsync(demandeId);
            if (demande is null || demande.Statut != MobileMoneyWithdrawalRequest.EnAttente) return false;

            // Le solde n'ayant jamais été touché à la création, le rejet ne nécessite
            // aucun remboursement — contrairement au dépôt (asymétrie volontaire).
            demande.Statut = MobileMoneyWithdrawalRequest.Rejete;
            demande.MotifRejet = motif.Trim();
            demande.DateTraitement = DateTime.UtcNow;
            demande.TraitePar = _user.Profil.Email;
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "RETRAIT_MOBILEMONEY_REJETE",
                Cible = $"UserId={demande.UserId}",
                Details = $"Ref: {demande.Reference}, Motif: {demande.MotifRejet}"
            });
            await ctx.SaveChangesAsync();

            await _notifHist.AddNotificationForUserAsync(demande.UserId,
                $"Votre demande de retrait Mobile Money {demande.Reference} a été rejetée : {demande.MotifRejet}", "ERROR", "RETRAIT");
            _logger.LogInformation("Retrait Mobile Money {Reference} rejeté par {AdminEmail} : {Motif}",
                demande.Reference, PiiMasker.MaskEmail(_user.Profil.Email), motif);
            return true;
        }

        // Référence courte, lisible au guichet, sans caractères ambigus (pas de 0/O, 1/I…).
        private static async Task<string> GenererReferenceUniqueAsync(BankDbContext ctx)
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            for (var essai = 0; essai < 10; essai++)
            {
                var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
                var suffixe = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
                var reference = $"ADN-{suffixe}";
                if (!await ctx.MobileMoneyWithdrawalRequests.AnyAsync(r => r.Reference == reference))
                    return reference;
            }
            throw new InvalidOperationException("Impossible de générer une référence de retrait unique.");
        }
    }
}
