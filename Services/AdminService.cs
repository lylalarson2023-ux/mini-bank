using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;
using BCrypt.Net;

namespace MBANK_ETUDIANT.Services
{
    public class AdminService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<AdminService> _logger;

        public AdminService(BankDbContext context, UserContext user, ILogger<AdminService> logger)
        {
            _context = context;
            _user = user;
            _logger = logger;
        }

        public async Task<List<UserProfile>> GetDossiersEnAttenteAsync()
        {
            if (!_user.Profil.IsAdmin) return new();
            return await _context.UserProfiles
                .Where(u => u.PendingPremiumUpgrade || u.PendingCreditRequest)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalBankBalanceAsync()
        {
            if (!_user.Profil.IsAdmin) return 0;
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
            _logger.LogInformation("Premium approuvé pour {Email} par admin {AdminEmail}", u.Email, _user.Profil.Email);
            return true;
        }

        public async Task<bool> ApprouverCredit(int id, decimal montantForce = 0)
        {
            if (!_user.Profil.IsAdmin) return false;
            var u = await _context.UserProfiles.FindAsync(id);
            if (u == null) return false;
            decimal montant = montantForce > 0 ? montantForce : u.PendingCreditAmount;
            u.Solde += montant;
            u.Dette += montant;
            u.PendingCreditRequest = false;
            u.PendingCreditAmount = 0;
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "CREDIT_APPROUVE",
                Cible = u.Email,
                Details = $"Montant: {montant} DH"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Crédit de {Montant} DH approuvé pour {Email} par {AdminEmail}",
                montant, u.Email, _user.Profil.Email);
            return true;
        }

        public async Task<bool> AdminDepot(int userId, decimal montant)
        {
            if (!_user.Profil.IsAdmin) return false;
            var u = await _context.UserProfiles.FindAsync(userId);
            if (u == null) return false;
            u.Solde += montant;
            _context.Transactions.Add(new Transaction
            {
                UserId = userId,
                Montant = montant,
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
                Details = $"Montant: {montant} DH"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Dépôt admin de {Montant} DH sur compte #{UserId} par {AdminEmail}",
                montant, userId, _user.Profil.Email);
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
            u.MotDePasse = ""; // clear plaintext legacy field
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "RESET_PASSWORD",
                Cible = u.Email,
                Details = "Mot de passe réinitialisé par l'administrateur"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Mot de passe réinitialisé pour {Email} par {AdminEmail}",
                u.Email, _user.Profil.Email);
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
            if (tuteur == null) return (false, "Aucun compte MBANK trouvé avec cet email");
            student.TuteurEmail = tuteurEmail.Trim().ToLower();
            student.TuteurAutorise = true;
            await _context.SaveChangesAsync();
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "ASSIGNER_TUTEUR",
                Cible = student.Email,
                Details = $"Tuteur assigné: {tuteurEmail}"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Tuteur {Tuteur} assigné à {Student} par admin {Admin}", tuteurEmail, student.Email, _user.Profil.Email);
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

        public async Task<(bool Success, string Message)> RejeterDossierKycAsync(int userId)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            var u = await _context.UserProfiles.FindAsync(userId);
            if (u == null) return (false, "Utilisateur introuvable");
            if (!u.PendingPremiumUpgrade) return (false, "Aucun dossier en attente pour cet utilisateur");

            u.PendingPremiumUpgrade = false;
            u.PremiumRejectedAt = DateTime.UtcNow;
            u.Solde += 100m;
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "REJET_KYC",
                Cible = u.Email,
                Details = "Dossier KYC rejeté, 100 DH remboursés"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Dossier KYC rejeté pour {Email} par admin {AdminEmail}", u.Email, _user.Profil.Email);
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
            await _context.SaveChangesAsync();
            _context.AdminLogs.Add(new AdminLog
            {
                Action = "REVOQUER_TUTEUR",
                Cible = student.Email,
                Details = $"Tuteur {oldTuteur} révoqué"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Tuteur de {Student} révoqué par admin {Admin}", student.Email, _user.Profil.Email);
            return (true, $"Tuteur révoqué pour {student.Email}");
        }
    }
}
