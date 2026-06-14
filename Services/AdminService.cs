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
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<AdminService> _logger;
        private readonly NotificationHistoryService _notifHist;

        public AdminService(BankDbContext context, UserContext user, ILogger<AdminService> logger, NotificationHistoryService notifHist)
        {
            _context = context;
            _user = user;
            _logger = logger;
            _notifHist = notifHist;
        }

        public async Task<List<UserProfile>> GetDossiersEnAttenteAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            return await _context.UserProfiles
                .Where(u => u.PendingPremiumUpgrade || u.PendingCreditRequest)
                .ToListAsync();
        }

        // ADR-001 : retourne des centimes (long)
        public async Task<long> GetTotalBankBalanceAsync()
        {
            if (!_user.Profil.IsAdmin) return 0L;
            return await _context.UserProfiles.SumAsync(u => u.Solde);
        }

        public async Task<List<AdminLog>> GetAdminLogsAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            return await _context.AdminLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(15)
                .ToListAsync();
        }

        public async Task<bool> RejeterCreditAsync(int userId, string motif)
        {
            if (!_user.Profil.IsAdmin) return false;

            var demande = await _context.CreditRequests
                .Where(c => c.UserId == userId && c.Statut == "EN_ATTENTE")
                .OrderByDescending(c => c.DateDemande)
                .FirstOrDefaultAsync();

            if (demande == null) return false;

            demande.Statut = "REJETE";
            demande.MotifRejet = motif;

            var u = await _context.UserProfiles.FindAsync(userId);
            if (u != null)
            {
                u.PendingCreditRequest = false;
                u.PendingCreditAmount = 0L;
            }

            _context.AdminLogs.Add(new AdminLog
            {
                Action = "CREDIT_REJETE",
                Cible = u?.Email ?? $"UserId:{userId}",
                Details = $"Motif: {motif}"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Crédit rejeté pour UserId={UserId} par {AdminEmail}: {Motif}",
                userId, PiiMasker.MaskEmail(_user.Profil.Email), motif);
            return true;
        }

        public async Task<bool> ApprouverPremium(int id)
        {
            if (!_user.Profil.IsAdmin) return false;
            var u = await _context.UserProfiles.FindAsync(id);
            if (u == null) return false;
            u.Statut = UserStatus.PREMIUM;
            u.PendingPremiumUpgrade = false;
            u.PremiumValidatedAt = DateTime.UtcNow;
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "UPGRADE_PREMIUM",
                Cible = u.Email,
                Details = "Passage au statut Premium validé"
            });
            await _context.SaveChangesAsync();
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
            var u = await _context.UserProfiles.FindAsync(userId);
            if (u == null) return false;

            var demande = await _context.CreditRequests
                .Where(c => c.UserId == userId && c.Statut == "EN_ATTENTE")
                .OrderByDescending(c => c.DateDemande)
                .FirstOrDefaultAsync();

            long montant = montantForceCentimes > 0L ? montantForceCentimes
                : demande?.Montant ?? u.PendingCreditAmount;

            u.Solde += montant;
            u.Dette += montant;
            u.PendingCreditRequest = false;
            u.PendingCreditAmount = 0L;

            if (demande != null)
                demande.Statut = "APPROUVE";

            _context.AdminLogs.Add(new AdminLog
            {
                Action = "CREDIT_APPROUVE",
                Cible = u.Email,
                Details = $"Montant: {montant.ToDh()}"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Crédit de {Montant} approuvé pour {Email} par {AdminEmail}",
                montant.ToDh(), PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        // ADR-001 : montant en centimes (long)
        public async Task<bool> AdminDepot(int userId, long montantCentimes)
        {
            if (!_user.Profil.IsAdmin) return false;
            var u = await _context.UserProfiles.FindAsync(userId);
            if (u == null) return false;
            u.Solde += montantCentimes;
            _context.Transactions.Add(new Transaction
            {
                UserId = userId,
                Montant = montantCentimes,
                Type = "DÉPÔT",
                Motif = "Dépôt administratif",
                SoldeApres = u.Solde,
                Libelle = "DÉPÔT ADMIN",
                Date = DateTime.UtcNow
            });
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "DEPOT_ADMIN",
                Cible = u.Email,
                Details = $"Montant: {montantCentimes.ToDh()}"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Dépôt admin de {Montant} sur compte #{UserId} par {AdminEmail}",
                montantCentimes.ToDh(), userId, PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<List<UserProfile>> SearchUsersAsync(string query)
        {
            if (!_user.Profil.IsAdmin) return new();
            return await _context.UserProfiles
                .Where(u => u.Email.Contains(query) || u.Nom.Contains(query) || u.Prenom.Contains(query))
                .Take(20)
                .ToListAsync();
        }

        public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
        {
            if (!_user.Profil.IsAdmin) return false;
            var u = await _context.UserProfiles.FindAsync(userId);
            if (u == null) return false;
            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            u.MotDePasse = "";
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "RESET_PASSWORD",
                Cible = u.Email,
                Details = "Mot de passe réinitialisé par l'administrateur"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Mot de passe réinitialisé pour {Email} par {AdminEmail}",
                PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<int> GetTotalUsersAsync()
        {
            if (!_user.Profil.IsAdmin) return 0;
            return await _context.UserProfiles.CountAsync();
        }

        public async Task<int> GetPendingPremiumCountAsync()
        {
            if (!_user.Profil.IsAdmin) return 0;
            return await _context.UserProfiles.CountAsync(u => u.PendingPremiumUpgrade);
        }

        public async Task<int> GetPendingCreditCountAsync()
        {
            if (!_user.Profil.IsAdmin) return 0;
            return await _context.UserProfiles.CountAsync(u => u.PendingCreditRequest);
        }

        // --- GESTION TUTEURS ---
        public async Task<List<UserProfile>> GetStudentsSansTuteurAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            return await _context.UserProfiles
                .Where(u => !u.IsAdmin && (string.IsNullOrEmpty(u.TuteurEmail) || !u.TuteurAutorise))
                .OrderByDescending(u => u.DateInscription)
                .Take(50)
                .ToListAsync();
        }

        public async Task<List<(UserProfile Student, UserProfile? Tuteur)>> GetRelationsTuteurAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            var students = await _context.UserProfiles
                .Where(u => !u.IsAdmin && u.TuteurAutorise && !string.IsNullOrEmpty(u.TuteurEmail))
                .ToListAsync();

            var tuteurEmails = students.Select(s => s.TuteurEmail).Distinct().ToList();
            var tuteurs = await _context.UserProfiles
                .Where(u => tuteurEmails.Contains(u.Email))
                .ToListAsync();

            return students.Select(s => (s, tuteurs.FirstOrDefault(t => t.Email == s.TuteurEmail))).ToList();
        }

        public async Task<(bool Success, string Message)> AssignerTuteurAsync(int studentId, string tuteurEmail)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            var student = await _context.UserProfiles.FindAsync(studentId);
            if (student == null) return (false, "Étudiant introuvable");
            if (string.IsNullOrWhiteSpace(tuteurEmail)) return (false, "Email tuteur requis");
            var tuteur = await _context.UserProfiles.FirstOrDefaultAsync(u => u.Email == tuteurEmail.Trim().ToLower());
            if (tuteur == null) return (false, "Aucun compte ADN_pay trouvé avec cet email");
            student.TuteurEmail = tuteurEmail.Trim().ToLower();
            student.TuteurAutorise = true;
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "ASSIGNER_TUTEUR",
                Cible = student.Email,
                Details = $"Tuteur assigné: {tuteurEmail}"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Tuteur {Tuteur} assigné à {Student} par admin {Admin}",
                PiiMasker.MaskEmail(tuteurEmail), PiiMasker.MaskEmail(student.Email),
                PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, $"Tuteur {tuteurEmail} assigné à {student.Email}");
        }

        public async Task<UserProfile?> GetUserByIdAsync(int userId)
        {
            if (!_user.Profil.IsAdmin) return null;
            return await _context.UserProfiles.FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<List<UserProfile>> GetHistoriqueDossiersAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            return await _context.UserProfiles
                .Where(u => u.PremiumValidatedAt != null || u.PremiumRejectedAt != null)
                .OrderByDescending(u => u.PremiumValidatedAt ?? u.PremiumRejectedAt)
                .Take(50)
                .ToListAsync();
        }

        // Remboursement KYC rejet : 100 DH = 10 000 centimes
        public async Task<(bool Success, string Message)> RejeterDossierKycAsync(int userId, string? motif = null)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            var u = await _context.UserProfiles.FindAsync(userId);
            if (u == null) return (false, "Utilisateur introuvable");
            if (!u.PendingPremiumUpgrade) return (false, "Aucun dossier en attente pour cet utilisateur");

            u.PendingPremiumUpgrade = false;
            u.PremiumRejectedAt = DateTime.UtcNow;
            u.KycRejetMotif = motif;
            u.Solde += 10_000L; // remboursement 100 DH
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "REJET_KYC",
                Cible = u.Email,
                Details = motif != null ? $"Dossier KYC rejeté : {motif}" : "Dossier KYC rejeté, 100 DH remboursés"
            });
            await _context.SaveChangesAsync();
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
            var student = await _context.UserProfiles.FindAsync(studentId);
            if (student == null) return (false, "Étudiant introuvable");
            var oldTuteur = student.TuteurEmail;
            student.TuteurEmail = "";
            student.TuteurAutorise = false;
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "REVOQUER_TUTEUR",
                Cible = student.Email,
                Details = $"Tuteur {oldTuteur} révoqué"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Tuteur de {Student} révoqué par admin {Admin}",
                PiiMasker.MaskEmail(student.Email), PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, $"Tuteur révoqué pour {student.Email}");
        }
    }
}
