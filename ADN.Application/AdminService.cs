using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;
using Microsoft.Extensions.Logging;
using BCrypt.Net;

namespace ADN_pay.Services
{
    public class AdminService
    {
        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;
        private readonly ILogger<AdminService> _logger;
        private readonly NotificationHistoryService _notifHist;

        // Fenêtre anti-double-clic / double-soumission pour les dépôts administratifs (secondes).
        // Un dépôt identique (même compte, même montant) déjà enregistré dans cette fenêtre est
        // considéré comme un doublon et ignoré, pour éviter de créditer deux fois.
        private const int DepotDoublonFenetreSecondes = 10;

        public AdminService(IDbContextFactory<BankDbContext> factory, UserContext user, ILogger<AdminService> logger, NotificationHistoryService notifHist)
        {
            _factory = factory;
            _user = user;
            _logger = logger;
            _notifHist = notifHist;
        }

        public async Task<List<UserProfile>> GetDossiersEnAttenteAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles
                .Where(u => u.PendingPremiumUpgrade || u.PendingCreditRequest)
                .ToListAsync();
        }

        // ADR-001 : retourne des centimes (long)
        public async Task<long> GetTotalBankBalanceAsync()
        {
            if (!_user.Profil.IsAdmin) return 0L;
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles.SumAsync(u => u.Solde);
        }

        public async Task<List<AdminLog>> GetAdminLogsAsync(int count = 15)
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.AdminLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }

        // Inscriptions par jour sur les N derniers jours (pour le graphique d'activité)
        public async Task<List<(DateTime Jour, int Count)>> GetRegistrationsPerDayAsync(int days = 14)
        {
            if (!_user.Profil.IsAdmin) return new();
            var today = DateTime.UtcNow.Date;
            var from = today.AddDays(-(days - 1));
            await using var ctx = await _factory.CreateDbContextAsync();
            var rows = await ctx.UserProfiles
                .Where(u => u.DateInscription >= from)
                .Select(u => u.DateInscription)
                .ToListAsync();
            var counts = rows.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Count());
            return Enumerable.Range(0, days)
                .Select(i => from.AddDays(i))
                .Select(jour => (jour, counts.GetValueOrDefault(jour, 0)))
                .ToList();
        }

        public async Task<bool> RejeterCreditAsync(int userId, string motif)
        {
            if (!_user.Profil.IsAdmin) return false;

            await using var ctx = await _factory.CreateDbContextAsync();
            var demande = await ctx.CreditRequests
                .Where(c => c.UserId == userId && c.Statut == "EN_ATTENTE")
                .OrderByDescending(c => c.DateDemande)
                .FirstOrDefaultAsync();

            if (demande == null) return false;

            demande.Statut = "REJETE";
            demande.MotifRejet = motif;

            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u != null)
            {
                u.PendingCreditRequest = false;
                u.PendingCreditAmount = 0L;
            }

            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "CREDIT_REJETE",
                Cible = u?.Email ?? $"UserId:{userId}",
                Details = $"Motif: {motif}"
            });
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Crédit rejeté pour UserId={UserId} par {AdminEmail}: {Motif}",
                userId, PiiMasker.MaskEmail(_user.Profil.Email), motif);
            return true;
        }

        public async Task<bool> ApprouverPremium(int id)
        {
            if (!_user.Profil.IsAdmin) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(id);
            if (u == null) return false;
            u.Statut = UserStatus.PREMIUM;
            u.PendingPremiumUpgrade = false;
            u.PremiumValidatedAt = DateTime.UtcNow;
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "UPGRADE_PREMIUM",
                Cible = u.Email,
                Details = "Passage au statut Premium validé"
            });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id,
                "Félicitations ! Votre dossier KYC a été validé. Vous êtes désormais Premium.", "SUCCESS", "KYC");
            _logger.LogInformation("Premium approuvé pour {Email} par admin {AdminEmail}",
                PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        // ADR-001 : montantForce en centimes (long)
        public async Task<bool> ApprouverCredit(int userId, long montantForceCentimes = 0L)
        {
            if (!_user.Profil.IsAdmin) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return false;

            var demande = await ctx.CreditRequests
                .Where(c => c.UserId == userId && c.Statut == "EN_ATTENTE")
                .OrderByDescending(c => c.DateDemande)
                .FirstOrDefaultAsync();

            bool estForce = montantForceCentimes > 0L;

            // Garde anti-double-soumission / idempotence : sans montant forcé légitime, on
            // n'approuve que s'il existe réellement une demande à traiter (demande EN_ATTENTE
            // ou PendingCreditRequest). Au 1er clic on remet PendingCreditRequest à false et on
            // passe la demande à APPROUVE ; un 2e clic ressort donc ici sans recréditer.
            if (!estForce && demande == null && !u.PendingCreditRequest)
                return false;

            long montant = estForce ? montantForceCentimes
                : demande?.Montant ?? u.PendingCreditAmount;

            // Rien à créditer (demande sans montant, montant forcé invalide) : on n'écrit pas.
            if (montant <= 0L) return false;

            u.Solde += montant;
            u.Dette += montant;
            u.PendingCreditRequest = false;
            u.PendingCreditAmount = 0L;

            if (demande != null)
                demande.Statut = "APPROUVE";

            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "CREDIT_APPROUVE",
                Cible = u.Email,
                Details = $"Montant: {montant.ToDh()}"
            });
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Crédit de {Montant} approuvé pour {Email} par {AdminEmail}",
                montant.ToDh(), PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        // ADR-001 : montant en centimes (long)
        public async Task<bool> AdminDepot(int userId, long montantCentimes)
        {
            if (!_user.Profil.IsAdmin) return false;
            if (montantCentimes <= 0L) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return false;

            // Garde anti-double-clic / double-soumission : si un dépôt admin identique (même
            // compte, même montant) a déjà été enregistré dans la fenêtre récente, on considère
            // qu'il s'agit d'un doublon et on ne crédite pas une 2e fois (idempotence serveur).
            var seuilDoublon = DateTime.UtcNow.AddSeconds(-DepotDoublonFenetreSecondes);
            bool doublon = await ctx.Transactions.AnyAsync(t =>
                t.UserId == userId
                && t.Libelle == "DÉPÔT ADMIN"
                && t.Montant == montantCentimes
                && t.Date >= seuilDoublon);
            if (doublon)
            {
                _logger.LogWarning("Dépôt admin en double ignoré : compte #{UserId}, {Montant}, par {AdminEmail}",
                    userId, montantCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email));
                return true; // idempotent : le 1er clic a déjà crédité ce montant
            }

            u.Solde += montantCentimes;
            ctx.Transactions.Add(new Transaction
            {
                UserId = userId,
                Montant = montantCentimes,
                Type = "DÉPÔT",
                Motif = "Dépôt administratif",
                SoldeApres = u.Solde,
                Libelle = "DÉPÔT ADMIN",
                Date = DateTime.UtcNow
            });
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "DEPOT_ADMIN",
                Cible = u.Email,
                Details = $"Montant: {montantCentimes.ToDh()}"
            });
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Dépôt admin de {Montant} sur compte #{UserId} par {AdminEmail}",
                montantCentimes.ToDh(), userId, PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<List<UserProfile>> SearchUsersAsync(string query)
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles
                .Where(u => u.Email.Contains(query) || u.Nom.Contains(query) || u.Prenom.Contains(query))
                .Take(20)
                .ToListAsync();
        }

        public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
        {
            if (!_user.Profil.IsAdmin) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return false;
            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            u.MotDePasse = "";
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "RESET_PASSWORD",
                Cible = u.Email,
                Details = "Mot de passe réinitialisé par l'administrateur"
            });
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Mot de passe réinitialisé pour {Email} par {AdminEmail}",
                PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<int> GetTotalUsersAsync()
        {
            if (!_user.Profil.IsAdmin) return 0;
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles.CountAsync();
        }

        public async Task<int> GetPendingPremiumCountAsync()
        {
            if (!_user.Profil.IsAdmin) return 0;
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles.CountAsync(u => u.PendingPremiumUpgrade);
        }

        public async Task<int> GetPendingCreditCountAsync()
        {
            if (!_user.Profil.IsAdmin) return 0;
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles.CountAsync(u => u.PendingCreditRequest);
        }

        // --- GESTION TUTEURS ---
        public async Task<List<UserProfile>> GetStudentsSansTuteurAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles
                .Where(u => !u.IsAdmin && (string.IsNullOrEmpty(u.TuteurEmail) || !u.TuteurAutorise))
                .OrderByDescending(u => u.DateInscription)
                .Take(50)
                .ToListAsync();
        }

        public async Task<List<(UserProfile Student, UserProfile? Tuteur)>> GetRelationsTuteurAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            var students = await ctx.UserProfiles
                .Where(u => !u.IsAdmin && u.TuteurAutorise && !string.IsNullOrEmpty(u.TuteurEmail))
                .ToListAsync();

            var tuteurEmails = students.Select(s => s.TuteurEmail).Distinct().ToList();
            var tuteurs = await ctx.UserProfiles
                .Where(u => tuteurEmails.Contains(u.Email))
                .ToListAsync();

            return students.Select(s => (s, tuteurs.FirstOrDefault(t => t.Email == s.TuteurEmail))).ToList();
        }

        public async Task<(bool Success, string Message)> AssignerTuteurAsync(int studentId, string tuteurEmail)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            await using var ctx = await _factory.CreateDbContextAsync();
            var student = await ctx.UserProfiles.FindAsync(studentId);
            if (student == null) return (false, "Étudiant introuvable");
            if (string.IsNullOrWhiteSpace(tuteurEmail)) return (false, "Email tuteur requis");
            var tuteur = await ctx.UserProfiles.FirstOrDefaultAsync(u => u.Email == tuteurEmail.Trim().ToLower());
            if (tuteur == null) return (false, "Aucun compte ADN_pay trouvé avec cet email");
            student.TuteurEmail = tuteurEmail.Trim().ToLower();
            student.TuteurAutorise = true;
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "ASSIGNER_TUTEUR",
                Cible = student.Email,
                Details = $"Tuteur assigné: {tuteurEmail}"
            });
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Tuteur {Tuteur} assigné à {Student} par admin {Admin}",
                PiiMasker.MaskEmail(tuteurEmail), PiiMasker.MaskEmail(student.Email),
                PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, $"Tuteur {tuteurEmail} assigné à {student.Email}");
        }

        public async Task<UserProfile?> GetUserByIdAsync(int userId)
        {
            if (!_user.Profil.IsAdmin) return null;
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<List<UserProfile>> GetHistoriqueDossiersAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.UserProfiles
                .Where(u => u.PremiumValidatedAt != null || u.PremiumRejectedAt != null)
                .OrderByDescending(u => u.PremiumValidatedAt ?? u.PremiumRejectedAt)
                .Take(50)
                .ToListAsync();
        }

        // Remboursement KYC rejet : 100 DH = 10 000 centimes
        public async Task<(bool Success, string Message)> RejeterDossierKycAsync(int userId, string? motif = null)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return (false, "Utilisateur introuvable");
            if (!u.PendingPremiumUpgrade) return (false, "Aucun dossier en attente pour cet utilisateur");

            u.PendingPremiumUpgrade = false;
            u.PremiumRejectedAt = DateTime.UtcNow;
            u.KycRejetMotif = motif;
            u.Solde += 10_000L; // remboursement 100 DH
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "REJET_KYC",
                Cible = u.Email,
                Details = motif != null ? $"Dossier KYC rejeté : {motif}" : "Dossier KYC rejeté, 100 DH remboursés"
            });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id,
                motif != null ? $"Votre dossier KYC a été rejeté : {motif}" : "Votre dossier KYC a été rejeté. 100 DH remboursés.",
                "ERROR", "KYC");
            _logger.LogInformation("Dossier KYC rejeté pour {Email} par admin {AdminEmail} : {Motif}",
                PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email), motif);
            return (true, $"Dossier KYC rejeté pour {u.Email}, 100 DH remboursés");
        }

        public async Task<(bool Success, string Message)> RevoquerTuteurParAdminAsync(int studentId)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            await using var ctx = await _factory.CreateDbContextAsync();
            var student = await ctx.UserProfiles.FindAsync(studentId);
            if (student == null) return (false, "Étudiant introuvable");
            var oldTuteur = student.TuteurEmail;
            student.TuteurEmail = "";
            student.TuteurAutorise = false;
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "REVOQUER_TUTEUR",
                Cible = student.Email,
                Details = $"Tuteur {oldTuteur} révoqué"
            });
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Tuteur de {Student} révoqué par admin {Admin}",
                PiiMasker.MaskEmail(student.Email), PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, $"Tuteur révoqué pour {student.Email}");
        }
    }
}
