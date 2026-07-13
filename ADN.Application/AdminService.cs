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
        private readonly IEmailSender _email;

        // Fenêtre anti-double-clic / double-soumission pour les dépôts administratifs (secondes).
        // Un dépôt identique (même compte, même montant) déjà enregistré dans cette fenêtre est
        // considéré comme un doublon et ignoré, pour éviter de créditer deux fois.
        private const int DepotDoublonFenetreSecondes = 10;

        public AdminService(IDbContextFactory<BankDbContext> factory, UserContext user, ILogger<AdminService> logger, NotificationHistoryService notifHist, IEmailSender email)
        {
            _factory = factory;
            _user = user;
            _logger = logger;
            _notifHist = notifHist;
            _email = email;
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
            try
            {
                var prenom = EmailTemplate.Escape(u.Prenom);
                var html = EmailTemplate.Wrap(
                    "Votre compte est désormais Premium 🎉",
                    EmailTemplate.Paragraphe($"Bonjour{(string.IsNullOrWhiteSpace(prenom) ? "" : " " + prenom)},")
                    + EmailTemplate.Paragraphe("Bonne nouvelle : votre dossier KYC a été <strong>validé</strong>. Votre compte ADN_pay passe au statut <strong>Premium</strong>.")
                    + EmailTemplate.Paragraphe("Vous bénéficiez désormais de plafonds relevés, de virements gratuits, de nouveaux designs de carte et des avantages Premium.")
                    + EmailTemplate.Bouton("Découvrir mes avantages", "https://adnpay.net/profil"),
                    preheader: "Votre dossier KYC est validé — vous êtes Premium.");
                await _email.SendAsync(u.Email, "ADN_pay — Votre compte est passé Premium", html,
                    "Votre dossier KYC a été validé : votre compte ADN_pay est désormais Premium.");
            }
            catch (Exception exMail) { _logger.LogWarning(exMail, "E-mail d'approbation KYC non envoyé (non bloquant)."); }
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

            // Trace le versement du crédit dans l'historique (entrée sur le compte courant).
            ctx.Transactions.Add(new Transaction
            {
                UserId = u.Id,
                Type = "CRÉDIT",
                Montant = montant,
                SoldeApres = u.Solde,
                Libelle = "Crédit accordé",
                Motif = "Crédit approuvé par l'administration"
            });
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "CREDIT_APPROUVE",
                Cible = u.Email,
                Details = $"Montant: {montant.ToDh()}"
            });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id,
                $"Votre crédit de {montant.ToDh()} a été approuvé et versé sur votre compte.", "SUCCESS", "CRÉDIT");
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

            // Notifie l'utilisateur côté client (notification persistée, lue par le web au
            // prochain chargement / ouverture de la cloche — les 2 apps partagent la base).
            if (u.NotifDepot)
                await _notifHist.AddNotificationForUserAsync(userId,
                    $"Dépôt de {montantCentimes.ToDh()} crédité sur votre compte par l'administration.",
                    "SUCCESS", "DEPOT");

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

        // Remboursement KYC rejet : 50 DH = 5 000 centimes
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
            u.Solde += 5_000L; // remboursement 50 DH
            ctx.AdminLogs.Add(new AdminLog
            {
                Action = "REJET_KYC",
                Cible = u.Email,
                Details = motif != null ? $"Dossier KYC rejeté : {motif}" : "Dossier KYC rejeté, 50 DH remboursés"
            });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id,
                motif != null ? $"Votre dossier KYC a été rejeté : {motif}" : "Votre dossier KYC a été rejeté. 50 DH remboursés.",
                "ERROR", "KYC");
            try
            {
                var prenom = EmailTemplate.Escape(u.Prenom);
                var motbefore = string.IsNullOrWhiteSpace(motif)
                    ? EmailTemplate.Paragraphe("Votre dossier KYC n'a pas pu être validé.")
                    : EmailTemplate.Paragraphe($"Votre dossier KYC n'a pas pu être validé pour la raison suivante :")
                      + EmailTemplate.Paragraphe($"<em>{EmailTemplate.Escape(motif)}</em>");
                var html = EmailTemplate.Wrap(
                    "Votre dossier KYC n'a pas été validé",
                    EmailTemplate.Paragraphe($"Bonjour{(string.IsNullOrWhiteSpace(prenom) ? "" : " " + prenom)},")
                    + motbefore
                    + EmailTemplate.Paragraphe("Les <strong>50 DH</strong> de frais de dossier vous ont été remboursés. Vous pouvez corriger et soumettre à nouveau votre dossier depuis votre profil.")
                    + EmailTemplate.Bouton("Reprendre mon dossier", "https://adnpay.net/profil"),
                    preheader: "Votre dossier KYC n'a pas été validé — 50 DH remboursés.");
                await _email.SendAsync(u.Email, "ADN_pay — Dossier KYC non validé", html,
                    (string.IsNullOrWhiteSpace(motif) ? "Votre dossier KYC a été rejeté." : $"Votre dossier KYC a été rejeté : {motif}.") + " 50 DH remboursés.");
            }
            catch (Exception exMail) { _logger.LogWarning(exMail, "E-mail de rejet KYC non envoyé (non bloquant)."); }
            _logger.LogInformation("Dossier KYC rejeté pour {Email} par admin {AdminEmail} : {Motif}",
                PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email), motif);
            return (true, $"Dossier KYC rejeté pour {u.Email}, 50 DH remboursés");
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

        // --- SCORING DES UTILISATEURS ---
        // Calcule, pour chaque utilisateur (hors admins), une série de sous-scores 0..100
        // normalisés sur le maximum de la population, puis un score composite. L'axe de tri
        // est choisi par l'admin (composite par défaut, ou valeur / engagement / risque).
        public async Task<List<ScoredUser>> GetUsersScoredAsync(ScoringMode mode = ScoringMode.Composite)
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();

            var users = await ctx.UserProfiles.Where(u => !u.IsAdmin).ToListAsync();
            if (users.Count == 0) return new();

            var now = DateTime.UtcNow;
            var since30 = now.AddDays(-30);

            // Épargne totale par utilisateur (somme des poches).
            var savings = (await ctx.SavingsPockets
                    .Select(p => new { p.UserId, p.MontantActuel })
                    .ToListAsync())
                .GroupBy(p => p.UserId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.MontantActuel));

            // Comptes de transactions (total + 30 derniers jours) par utilisateur.
            var txRows = await ctx.Transactions
                .Select(t => new { t.UserId, t.Date })
                .ToListAsync();
            var txByUser = txRows
                .GroupBy(t => t.UserId)
                .ToDictionary(g => g.Key, g => (Total: g.Count(), Recent: g.Count(t => t.Date >= since30)));

            // 1ʳᵉ passe : valeurs brutes.
            var scored = users.Select(u =>
            {
                var epargne = savings.GetValueOrDefault(u.Id);
                var (nbTx, nbTx30) = txByUser.TryGetValue(u.Id, out var c) ? c : (0, 0);
                return new ScoredUser
                {
                    User = u,
                    EpargneTotale = epargne,
                    NbTransactions = nbTx,
                    NbTransactions30j = nbTx30,
                    AncienneteJours = Math.Max(0, (int)(now - u.DateInscription).TotalDays),
                };
            }).ToList();

            // Maxima de la population pour normaliser (évite la division par zéro).
            double maxValeur = Math.Max(1, scored.Max(s => (double)(s.User.Solde + s.EpargneTotale)));
            double maxEngagement = Math.Max(1, scored.Max(s => (double)(s.NbTransactions + 2 * s.NbTransactions30j)));
            double maxFidelite = Math.Max(1, scored.Max(s => (double)s.AncienneteJours));
            double maxRisque = Math.Max(1, scored.Max(s => (double)RisqueBrut(s)));

            foreach (var s in scored)
            {
                s.ScoreValeur = Math.Round((s.User.Solde + s.EpargneTotale) / maxValeur * 100, 1);
                s.ScoreEngagement = Math.Round((s.NbTransactions + 2 * s.NbTransactions30j) / maxEngagement * 100, 1);
                s.ScoreFidelite = Math.Round(s.AncienneteJours / maxFidelite * 100, 1);
                s.ScoreStatut = s.User.Statut switch
                {
                    UserStatus.VIP => 100,
                    UserStatus.PREMIUM => 70,
                    _ => 30
                };
                s.ScoreRisque = Math.Round(RisqueBrut(s) / maxRisque * 100, 1);

                // Composite : valeur 40 %, engagement 30 %, fidélité 20 %, statut 10 %.
                s.ScoreComposite = Math.Round(
                    0.40 * s.ScoreValeur +
                    0.30 * s.ScoreEngagement +
                    0.20 * s.ScoreFidelite +
                    0.10 * s.ScoreStatut, 1);
            }

            return scored.OrderByDescending(s => s.ScorePour(mode)).ThenBy(s => s.User.Id).ToList();
        }

        // Risque brut : dette pondérée par le ratio dette / actifs (un compte très endetté
        // au regard de ce qu'il possède est plus risqué qu'un endettement couvert par l'épargne).
        private static double RisqueBrut(ScoredUser s)
        {
            if (s.User.Dette <= 0) return 0;
            double actifs = s.User.Solde + s.EpargneTotale;
            double ratio = s.User.Dette / (actifs + 1d); // +1 : garde le ratio fini
            return s.User.Dette * (1 + Math.Min(ratio, 3)); // plafonne l'effet du ratio
        }

        // --- VUE GLOBALE DES TRANSACTIONS (point 4) ---
        public record AdminTxView(
            int Id, int UserId, string UserNom, string UserEmail,
            string Type, long Montant, long Frais, long SoldeApres,
            string Libelle, string Motif, DateTime Date);

        public record AdminTxResult(
            List<AdminTxView> Items, int Total, long TotalEntrees, long TotalSorties);

        // Toutes les transactions de tous les utilisateurs, filtrables et paginées.
        public async Task<AdminTxResult> GetAllTransactionsAsync(
            int? userId = null, string? type = null,
            DateTime? from = null, DateTime? to = null,
            int page = 1, int pageSize = 50)
        {
            if (!_user.Profil.IsAdmin) return new(new(), 0, 0, 0);
            pageSize = Math.Clamp(pageSize, 1, 200);
            page = Math.Max(1, page);

            await using var ctx = await _factory.CreateDbContextAsync();
            var q = ctx.Transactions.AsQueryable();
            if (userId.HasValue) q = q.Where(t => t.UserId == userId.Value);
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(t => t.Type == type);
            if (from.HasValue) q = q.Where(t => t.Date >= from.Value);
            if (to.HasValue) q = q.Where(t => t.Date <= to.Value);

            var total = await q.CountAsync();

            // Totaux entrées/sorties : IsEntree/IsSortie ne sont pas traduisibles en SQL
            // (NotMapped) → on agrège côté mémoire sur le sous-ensemble filtré.
            var typesMontants = await q.Select(t => new { t.Type, t.Montant }).ToListAsync();
            long totalEntrees = typesMontants
                .Where(t => new Transaction { Type = t.Type }.IsEntree).Sum(t => t.Montant);
            long totalSorties = typesMontants
                .Where(t => new Transaction { Type = t.Type }.IsSortie).Sum(t => t.Montant);

            var items = await q
                .OrderByDescending(t => t.Date)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Join(ctx.UserProfiles,
                    t => t.UserId, u => u.Id,
                    (t, u) => new AdminTxView(
                        t.Id, t.UserId, u.Prenom + " " + u.Nom, u.Email,
                        t.Type, t.Montant, t.Frais, t.SoldeApres,
                        t.Libelle, t.Motif, t.Date))
                .ToListAsync();

            return new(items, total, totalEntrees, totalSorties);
        }

        // --- POUVOIRS ADMIN SUR LES COMPTES ---

        public async Task<bool> ChangerStatutAsync(int userId, UserStatus statut)
        {
            if (!_user.Profil.IsAdmin) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return false;
            u.Statut = statut;
            ctx.AdminLogs.Add(new AdminLog { Action = "CHANGER_STATUT", Cible = u.Email, Details = $"Nouveau statut : {statut}" });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id, $"Votre statut a été mis à jour : {statut}.", "INFO", "COMPTE");
            _logger.LogInformation("Statut de {Email} changé en {Statut} par {Admin}",
                PiiMasker.MaskEmail(u.Email), statut, PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<(bool Success, string Message)> SetPlafondsAsync(int userId, long journalierCentimes, long mensuelCentimes)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            if (journalierCentimes <= 0 || mensuelCentimes <= 0) return (false, "Les plafonds doivent être positifs");
            if (journalierCentimes > mensuelCentimes) return (false, "Le plafond journalier ne peut pas dépasser le mensuel");
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return (false, "Utilisateur introuvable");
            u.PlafondJournalier = journalierCentimes;
            u.PlafondMensuel = mensuelCentimes;
            ctx.AdminLogs.Add(new AdminLog { Action = "SET_PLAFONDS", Cible = u.Email, Details = $"{journalierCentimes.ToDh()}/jour, {mensuelCentimes.ToDh()}/mois" });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id,
                $"Vos plafonds ont été mis à jour : {journalierCentimes.ToDh()}/jour, {mensuelCentimes.ToDh()}/mois.", "INFO", "PLAFOND");
            _logger.LogInformation("Plafonds de {Email} définis par admin {Admin} : {J}/{M}",
                PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(_user.Profil.Email), journalierCentimes.ToDh(), mensuelCentimes.ToDh());
            return (true, "Plafonds mis à jour");
        }

        // Ajuste le solde : delta positif = crédit, négatif = débit. Tracé en transaction + log.
        public async Task<(bool Success, string Message)> AjusterSoldeAsync(int userId, long deltaCentimes, string motif)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            if (deltaCentimes == 0) return (false, "Le montant ne peut pas être nul");
            if (string.IsNullOrWhiteSpace(motif)) return (false, "Un motif est requis");
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return (false, "Utilisateur introuvable");
            if (deltaCentimes < 0 && u.Solde + deltaCentimes < 0) return (false, "Solde insuffisant pour ce débit");
            u.Solde += deltaCentimes;
            ctx.Transactions.Add(new Transaction
            {
                UserId = u.Id,
                Type = deltaCentimes > 0 ? "DÉPÔT" : "RETRAIT",
                Montant = Math.Abs(deltaCentimes),
                SoldeApres = u.Solde,
                Libelle = deltaCentimes > 0 ? "Ajustement admin (crédit)" : "Ajustement admin (débit)",
                Motif = motif
            });
            ctx.AdminLogs.Add(new AdminLog { Action = "AJUSTER_SOLDE", Cible = u.Email, Details = $"{deltaCentimes.ToDh()} — {motif}" });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id,
                $"Ajustement de votre solde : {deltaCentimes.ToDh()} ({motif}).",
                deltaCentimes > 0 ? "SUCCESS" : "INFO", deltaCentimes > 0 ? "DEPOT" : "RETRAIT");
            _logger.LogInformation("Solde de {Email} ajusté de {Delta} par admin {Admin} : {Motif}",
                PiiMasker.MaskEmail(u.Email), deltaCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email), motif);
            return (true, "Solde ajusté");
        }

        public async Task<(bool Success, string Message)> SetBlocageAsync(int userId, bool bloquer)
        {
            if (!_user.Profil.IsAdmin) return (false, "Accès refusé");
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return (false, "Utilisateur introuvable");
            if (u.IsAdmin) return (false, "Impossible de bloquer un compte administrateur");
            u.Bloque = bloquer;
            ctx.AdminLogs.Add(new AdminLog { Action = bloquer ? "BLOQUER_COMPTE" : "DEBLOQUER_COMPTE", Cible = u.Email, Details = bloquer ? "Compte bloqué" : "Compte débloqué" });
            await ctx.SaveChangesAsync();
            await _notifHist.AddNotificationForUserAsync(u.Id,
                bloquer ? "Votre compte a été suspendu. Contactez le support." : "Votre compte a été réactivé.",
                bloquer ? "ERROR" : "SUCCESS", "COMPTE");
            _logger.LogInformation("Compte {Email} {Action} par admin {Admin}",
                PiiMasker.MaskEmail(u.Email), bloquer ? "bloqué" : "débloqué", PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, bloquer ? "Compte bloqué" : "Compte débloqué");
        }

        public async Task<List<SavingsPocket>> GetUserSavingsAsync(int userId)
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.SavingsPockets.Where(p => p.UserId == userId).ToListAsync();
        }

        public async Task<List<CreditRequest>> GetUserCreditsAsync(int userId)
        {
            if (!_user.Profil.IsAdmin) return new();
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.CreditRequests.Where(c => c.UserId == userId)
                .OrderByDescending(c => c.DateDemande).ToListAsync();
        }
    }
}
