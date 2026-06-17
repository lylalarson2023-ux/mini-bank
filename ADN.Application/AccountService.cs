using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;

namespace ADN_pay.Services
{
    public class AccountService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<AccountService> _logger;
        private readonly NotificationHistoryService _notifHist;
        private readonly IEmailSender _email;

        public AccountService(BankDbContext context, UserContext user, ILogger<AccountService> logger,
            NotificationHistoryService notifHist, IEmailSender email)
        {
            _context = context;
            _user = user;
            _logger = logger;
            _notifHist = notifHist;
            _email = email;
        }

        // ADR-001 : montantCentimes en long
        public async Task<bool> ExecuterOperationAsync(long montantCentimes, string motif, string type = "VIREMENT")
        {
            if (montantCentimes <= 0 || ((type == "RETRAIT" || type == "VIREMENT") && _user.Profil.Solde < montantCentimes))
            {
                _logger.LogWarning("{Type} refusé : montant={Montant}, solde={Solde}",
                    type, montantCentimes.ToDh(), _user.Profil.Solde.ToDh());
                return false;
            }
            if (type == "RETRAIT" || type == "VIREMENT")
            {
                var (allow, msg) = VerifierPlafond(montantCentimes);
                if (!allow)
                {
                    _logger.LogWarning("{Type} refusé : {Msg}", type, msg);
                    return false;
                }
            }
            var user = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (user == null) return false;

            if (type == "RETRAIT" || type == "VIREMENT")
            {
                user.Solde -= montantCentimes;
                user.MontantJournalierUtilise += montantCentimes;
                user.MontantMensuelUtilise += montantCentimes;
            }
            else user.Solde += montantCentimes;

            user.NombreTransactions++;
            _context.Transactions.Add(new Transaction
            {
                UserId = user.Id,
                Montant = montantCentimes,
                Type = type,
                Motif = motif,
                SoldeApres = user.Solde,
                Libelle = $"{type}: {motif}",
                Date = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            _user.Profil.Solde = user.Solde;
            _user.Profil.MontantJournalierUtilise = user.MontantJournalierUtilise;
            _user.Profil.MontantMensuelUtilise = user.MontantMensuelUtilise;

            var label = type switch { "DÉPÔT" => "Dépôt", "RETRAIT" => "Retrait", "VIREMENT" => "Virement", _ => type };
            await _notifHist.AddNotificationAsync(
                $"{label} de {montantCentimes.ToDh()} — {motif}", "SUCCESS", type);

            _logger.LogInformation("{Type} de {Montant} effectué par {Email}",
                type, montantCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<List<Transaction>> GetHistoriqueAsync()
            => await _context.Transactions
                .Where(t => t.UserId == _user.Profil.Id)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

        public async Task<long> GetBalanceAsync()
        {
            var account = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            return account?.Solde ?? 0L;
        }

        public async Task<List<Transaction>> GetRecentTransactionsAsync(int count)
            => await _context.Transactions
                .Where(t => t.UserId == _user.Profil.Id)
                .OrderByDescending(t => t.Date)
                .Take(count)
                .ToListAsync();

        // ADR-001 : tous les montants retournés en centimes (long)
        public async Task<(long RevenusMois, long DepensesMois, long TotalEpargne,
            List<(DateTime Jour, long Entrees, long Sorties)> DailyBreakdown)> GetDashboardStatsAsync()
        {
            var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var transactionsMois = await _context.Transactions
                .Where(t => t.UserId == _user.Profil.Id && t.Date >= debutMois)
                .ToListAsync();

            var revenus  = transactionsMois.Where(t => t.IsEntree).Sum(t => t.Montant);
            var depenses = transactionsMois.Where(t => t.IsSortie).Sum(t => t.Montant);

            var totalEpargne = await _context.SavingsPockets
                .Where(p => p.UserId == _user.Profil.Id)
                .SumAsync(p => p.MontantActuel);

            var septJours = DateTime.UtcNow.Date.AddDays(-6);
            var daily = Enumerable.Range(0, 7).Select(offset =>
            {
                var day = septJours.AddDays(offset);
                var dayTx = transactionsMois.Where(t => t.Date.Date == day).ToList();
                return (
                    Jour: day,
                    Entrees: dayTx.Where(t => t.IsEntree).Sum(t => t.Montant),
                    Sorties: dayTx.Where(t => t.IsSortie).Sum(t => t.Montant)
                );
            }).ToList();

            return (revenus, depenses, totalEpargne, daily);
        }

        // ADR-001 : montantCentimes en long
        public async Task<bool> EffectuerVirementAsync(string emailDestinataire, long montantCentimes, string motif)
        {
            var (allow, msg) = VerifierPlafond(montantCentimes);
            if (!allow) return false;

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var sender = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (sender == null || sender.Solde < montantCentimes) return false;

                var targetEmail = emailDestinataire.Trim().ToLower();
                if (sender.Email.ToLower() == targetEmail) return false;

                var recipient = await _context.UserProfiles.FirstOrDefaultAsync(u => u.Email == targetEmail);
                if (recipient == null) return false;

                sender.Solde -= montantCentimes;
                sender.MontantJournalierUtilise += montantCentimes;
                sender.MontantMensuelUtilise += montantCentimes;
                recipient.Solde += montantCentimes;
                sender.NombreTransactions++;
                recipient.NombreTransactions++;

                _context.Transactions.Add(new Transaction
                {
                    UserId = sender.Id,
                    Montant = montantCentimes,
                    Type = "VIREMENT",
                    Motif = motif,
                    SoldeApres = sender.Solde,
                    Libelle = $"Virement vers {recipient.Email}",
                    Date = DateTime.UtcNow
                });
                _context.Transactions.Add(new Transaction
                {
                    UserId = recipient.Id,
                    Montant = montantCentimes,
                    Type = "RÉCEPTION",
                    Motif = $"Virement de {sender.Email}",
                    SoldeApres = recipient.Solde,
                    Libelle = $"Virement reçu de {sender.Email}",
                    Date = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                _user.Profil.Solde = sender.Solde;
                _user.Profil.MontantJournalierUtilise = sender.MontantJournalierUtilise;
                _user.Profil.MontantMensuelUtilise = sender.MontantMensuelUtilise;
                _logger.LogInformation("Virement de {Montant} de {Sender} vers {Recipient}",
                    montantCentimes.ToDh(), PiiMasker.MaskEmail(sender.Email), PiiMasker.MaskEmail(recipient.Email));

                await _notifHist.AddNotificationForUserAsync(recipient.Id,
                    $"Virement reçu de {sender.Email} : {montantCentimes.ToDh()}", "SUCCESS", "VIREMENT");

                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> DefinirTuteur(string email)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return false;
            u.TuteurEmail = email;
            u.TuteurAutorise = true;
            _user.Profil.TuteurEmail = email;
            _user.Profil.TuteurAutorise = true;
            await _context.SaveChangesAsync();
            await _notifHist.AddNotificationAsync($"Tuteur autorisé : {email}", "SUCCESS", "TUTEUR");
            _logger.LogInformation("Tuteur {TuteurEmail} autorisé pour {Email}",
                PiiMasker.MaskEmail(email), PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<bool> RevoquerTuteur()
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return false;
            u.TuteurEmail = "";
            u.TuteurAutorise = false;
            _user.Profil.TuteurEmail = "";
            _user.Profil.TuteurAutorise = false;
            await _context.SaveChangesAsync();
            await _notifHist.AddNotificationAsync("Tuteur révoqué", "INFO", "TUTEUR");
            _logger.LogInformation("Tuteur révoqué pour {Email}", PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        // --- PARAMÈTRES COMPTE ---
        // L'e-mail n'est PAS modifié ici : il passe par le flux vérifié (RequestEmailChange/ConfirmEmailChange).
        public async Task<(bool Success, string Message)> UpdateProfileAsync(string nom, string prenom, string telephone, string email, string? currentPasswordForEmail = null)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");

            u.Nom = nom?.Trim() ?? "";
            u.Prenom = prenom?.Trim() ?? "";
            u.Telephone = telephone?.Trim() ?? "";

            await _context.SaveChangesAsync();

            _user.Profil.Nom = u.Nom;
            _user.Profil.Prenom = u.Prenom;
            _user.Profil.Telephone = u.Telephone;

            await _notifHist.AddNotificationAsync("Profil mis à jour", "SUCCESS", "PROFIL");
            _logger.LogInformation("Profil mis à jour pour {Email}", PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, "Profil mis à jour avec succès");
        }

        // Étape 1 : demande de changement d'e-mail. Vérifie le mot de passe, envoie un code à la NOUVELLE adresse.
        public async Task<(bool Success, string Message)> RequestEmailChangeAsync(string newEmail, string currentPassword)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");

            var emailLower = (newEmail ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(emailLower) || !emailLower.Contains('@'))
                return (false, "Adresse e-mail invalide.");
            if (emailLower == u.Email)
                return (false, "Cette adresse est déjà la vôtre.");
            if (string.IsNullOrEmpty(currentPassword) || !BCrypt.Net.BCrypt.Verify(currentPassword, u.MotDePasseHash))
                return (false, "Mot de passe actuel incorrect.");
            if (await _context.UserProfiles.AnyAsync(x => x.Email == emailLower))
                return (false, "Cet e-mail est déjà utilisé.");

            var code = Random.Shared.Next(100000, 1000000).ToString();
            u.PendingEmail = emailLower;
            u.EmailChangeCodeHash = BCrypt.Net.BCrypt.HashPassword(code);
            u.EmailChangeCodeExpiry = DateTime.UtcNow.AddMinutes(15);
            await _context.SaveChangesAsync();

            var html = $@"<p>Bonjour,</p>
<p>Vous avez demandé à associer cette adresse à votre compte <strong>ADN_pay</strong>.</p>
<p>Votre code de confirmation est : <strong style=""font-size:1.4rem;letter-spacing:3px;"">{code}</strong></p>
<p>Ce code expire dans 15 minutes. Si vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail.</p>
<p>— L'équipe ADN_pay</p>";
            await _email.SendAsync(emailLower, "ADN_pay — Confirmez votre nouvelle adresse e-mail", html,
                $"Votre code de confirmation ADN_pay : {code} (valable 15 minutes).");

            _logger.LogInformation("Code de changement d'e-mail envoyé pour {Email} → {New}",
                PiiMasker.MaskEmail(u.Email), PiiMasker.MaskEmail(emailLower));
            return (true, $"Un code de confirmation a été envoyé à {emailLower}.");
        }

        // Étape 2 : confirmation du code → applique le changement d'e-mail.
        public async Task<(bool Success, string Message)> ConfirmEmailChangeAsync(string code)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");
            if (string.IsNullOrEmpty(u.PendingEmail) || string.IsNullOrEmpty(u.EmailChangeCodeHash))
                return (false, "Aucune demande de changement d'e-mail en cours.");
            if (u.EmailChangeCodeExpiry < DateTime.UtcNow)
            {
                u.PendingEmail = null; u.EmailChangeCodeHash = null; u.EmailChangeCodeExpiry = null;
                await _context.SaveChangesAsync();
                return (false, "Le code a expiré. Recommencez la demande.");
            }
            if (string.IsNullOrWhiteSpace(code) || !BCrypt.Net.BCrypt.Verify(code.Trim(), u.EmailChangeCodeHash))
                return (false, "Code incorrect.");
            // Double-vérification d'unicité (au cas où l'e-mail aurait été pris entre-temps)
            if (await _context.UserProfiles.AnyAsync(x => x.Email == u.PendingEmail))
                return (false, "Cet e-mail vient d'être utilisé par un autre compte.");

            var ancienEmail = u.Email;
            u.Email = u.PendingEmail!;
            u.PendingEmail = null; u.EmailChangeCodeHash = null; u.EmailChangeCodeExpiry = null;
            await _context.SaveChangesAsync();
            _user.Profil.Email = u.Email;

            // Avertit l'ancienne adresse (bonne pratique de sécurité)
            await _email.SendAsync(ancienEmail, "ADN_pay — Votre adresse e-mail a été modifiée",
                $@"<p>L'adresse e-mail de votre compte ADN_pay a été remplacée par <strong>{u.Email}</strong>.</p>
<p>Si vous n'êtes pas à l'origine de ce changement, contactez-nous immédiatement.</p>");

            await _notifHist.AddNotificationAsync("Adresse e-mail mise à jour", "SUCCESS", "PROFIL");
            _logger.LogInformation("E-mail changé : {Old} → {New}", PiiMasker.MaskEmail(ancienEmail), PiiMasker.MaskEmail(u.Email));
            return (true, "Votre adresse e-mail a été mise à jour.");
        }

        public async Task<(bool Success, string Message)> ChangerMotDePasseAsync(string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return (false, "Le nouveau mot de passe est requis");

            if (newPassword.Length < 8)
                return (false, "Le mot de passe doit contenir au moins 8 caractères");

            if (!newPassword.Any(char.IsUpper))
                return (false, "Le mot de passe doit contenir au moins une majuscule");

            if (!newPassword.Any(char.IsLower))
                return (false, "Le mot de passe doit contenir au moins une minuscule");

            if (!newPassword.Any(char.IsDigit))
                return (false, "Le mot de passe doit contenir au moins un chiffre");

            if (!newPassword.Any(c => !char.IsLetterOrDigit(c)))
                return (false, "Le mot de passe doit contenir au moins un caractère spécial (!@#$%^&*)");

            var commonPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Password123!", "12345678", "Azerty123", "Motdepasse1", "Admin123!", "Test1234!",
                "123456789", "Password1", "Qwerty123", "Aa123456", "Abcd1234", "Hello123",
                "Welcome1", "Passer123", "00000000", "11111111", "Mot2passe", "P@ssword1",
                "Password123", "Password12", "azertyuiop", "123456", "motdepasse", "admin",
                "1234", "azerty", "0000", "passer", "soleil", "1234567890", "abc123",
                "Password!", "P@ssw0rd", "PASSWORD", "Passw0rd", "qwerty123", "azerty123"
            };
            if (commonPasswords.Contains(newPassword))
                return (false, "Ce mot de passe est trop commun. Veuillez en choisir un plus sécurisé.");

            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, u.MotDePasseHash))
                return (false, "Mot de passe actuel incorrect");

            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _context.SaveChangesAsync();

            await _notifHist.AddNotificationAsync("Mot de passe changé", "SUCCESS", "PROFIL");
            _logger.LogInformation("Mot de passe changé pour {Email}", PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, "Mot de passe changé avec succès");
        }

        // --- EXPORT DES DONNÉES (RGPD / Loi 09-08) ---
        public async Task<string> ExportPersonalDataAsync()
        {
            var u = await _context.UserProfiles
                .Include(x => x.Transactions)
                .Include(x => x.SavingsPockets)
                .FirstOrDefaultAsync(x => x.Id == _user.Profil.Id);

            if (u == null) return "";

            var export = new System.Text.StringBuilder();
            export.AppendLine("=== EXPORT DES DONNEES PERSONNELLES - ADN_pay ===");
            export.AppendLine($"Date d'export : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            export.AppendLine($"Conforme à la Loi 09-08 et au RGPD");
            export.AppendLine();
            export.AppendLine("--- IDENTITE ---");
            export.AppendLine($"Nom : {u.Nom}");
            export.AppendLine($"Prénom : {u.Prenom}");
            export.AppendLine($"Email : {u.Email}");
            export.AppendLine($"Téléphone : {u.Telephone}");
            export.AppendLine($"Date de naissance : {u.DateNaissance?.ToString("yyyy-MM-dd") ?? "N/A"}");
            export.AppendLine($"Nationalité : {u.Nationalite}");
            export.AppendLine($"Ville : {u.Ville}");
            export.AppendLine($"Adresse : {u.AdresseCasablanca}");
            export.AppendLine();
            export.AppendLine("--- DONNEES ACADEMIQUES ---");
            export.AppendLine($"Établissement : {u.Etablissement}");
            export.AppendLine($"Filière : {u.Filiere}");
            export.AppendLine($"Niveau : {u.NiveauEtude}");
            export.AppendLine($"Matricule : {u.MatriculeEtudiant}");
            export.AppendLine();
            export.AppendLine("--- DONNEES FINANCIERES ---");
            export.AppendLine($"Solde : {u.Solde.ToDh()}");
            export.AppendLine($"Dette : {u.Dette.ToDh()}");
            export.AppendLine($"Statut : {u.Statut}");
            export.AppendLine($"Date d'inscription : {u.DateInscription:yyyy-MM-dd HH:mm}");
            export.AppendLine($"Nombre de transactions : {u.NombreTransactions}");
            export.AppendLine();
            export.AppendLine("--- HISTORIQUE DES TRANSACTIONS ---");
            foreach (var t in u.Transactions.OrderByDescending(x => x.Date))
            {
                export.AppendLine($"[{t.Date:yyyy-MM-dd HH:mm}] {t.Type} - {t.Motif} - {t.Montant.ToDh()}");
            }
            export.AppendLine();
            export.AppendLine("--- POCKETS D'EPARGNE ---");
            foreach (var p in u.SavingsPockets)
            {
                export.AppendLine($"{p.Objectif} : {p.MontantActuel.ToDh()} (fin : {p.Cible:yyyy-MM-dd})");
            }
            export.AppendLine();
            export.AppendLine("--- TUTEUR ---");
            export.AppendLine($"Email tuteur : {u.TuteurEmail}");
            export.AppendLine($"Autorisation : {(u.TuteurAutorise ? "OUI" : "NON")}");
            export.AppendLine();
            export.AppendLine("--- FIN DE L'EXPORT ---");
            return export.ToString();
        }

        // --- SUPPRESSION DE COMPTE (droit à l'effacement) ---
        // Clôture conforme : droit à l'effacement (RGPD/Loi 09-08) avec conservation des
        // écritures financières imposée par la lutte anti-blanchiment (loi 43-05).
        public async Task<(bool Success, string Message)> SupprimerCompteAsync()
        {
            var u = await _context.UserProfiles
                .Include(x => x.SavingsPockets)
                .FirstOrDefaultAsync(x => x.Id == _user.Profil.Id);

            if (u == null) return (false, "Utilisateur introuvable");
            if (u.CompteCloture) return (false, "Ce compte est déjà clôturé.");
            if (u.Solde > 0)
                return (false, "Veuillez retirer ou transférer la totalité de votre solde avant la clôture.");
            if (u.Dette > 0)
                return (false, "Vous avez un crédit en cours. Remboursez votre dette avant de clôturer le compte.");
            var epargne = u.SavingsPockets.Sum(p => p.MontantActuel);
            if (epargne > 0)
                return (false, "Récupérez d'abord les fonds de vos poches d'épargne avant la clôture.");

            _logger.LogWarning("CLÔTURE COMPTE : {Email} (Id={Id})", PiiMasker.MaskEmail(u.Email), u.Id);

            // Anonymisation des données personnelles
            u.Nom = "Compte"; u.Prenom = "Clôturé";
            u.Email = $"cloture-{u.Id}@adnpay.invalid";
            u.Telephone = ""; u.AdresseCasablanca = ""; u.Ville = ""; u.CodePostal = "";
            u.DateNaissance = null; u.LieuNaissance = ""; u.Nationalite = ""; u.Genre = "";
            u.PassportOuCIN = ""; u.SituationMatrimoniale = "";
            u.Etablissement = ""; u.Filiere = ""; u.NiveauEtude = ""; u.AnneeEtude = ""; u.MatriculeEtudiant = "";
            u.StatutEtudiant = ""; u.ReseauPrincipal = "";
            u.DocIdentiteUrl = ""; u.DocDomicileUrl = ""; u.DocScolariteUrl = ""; u.PhotoUrl = ""; u.CvUrl = "";
            u.TuteurEmail = ""; u.TuteurAutorise = false;
            u.TwoFactorEnabled = false; u.TwoFactorSecret = null;
            foreach (var p in u.SavingsPockets) p.Objectif = "Clôturé";

            // Rend le compte définitivement inaccessible et invalide les sessions ouvertes
            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));
            u.SecurityStamp = Guid.NewGuid().ToString("N");
            u.CompteCloture = true;
            u.DateCloture = DateTime.UtcNow;

            // Les Transactions sont CONSERVÉES (obligation de rétention AML, loi 43-05).
            await _context.SaveChangesAsync();

            _user.Profil = new();
            _user.EstConnecte = false;

            _logger.LogInformation("Compte clôturé et anonymisé : Id={Id}", u.Id);
            return (true, "Votre compte a été clôturé et vos données personnelles anonymisées. Vous allez être déconnecté.");
        }

        // --- KYC --- frais = 100 DH = 10 000 centimes
        public async Task<bool> SoumettreDossierKYC(UserProfile kyc)
        {
            if (_user.Profil.Solde < 10_000L) return false;
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null) return false;
                u.Nom = kyc.Nom;
                u.Prenom = kyc.Prenom;
                u.DateNaissance = kyc.DateNaissance;
                u.LieuNaissance = kyc.LieuNaissance;
                u.Nationalite = kyc.Nationalite;
                u.PassportOuCIN = kyc.PassportOuCIN;
                u.SituationMatrimoniale = kyc.SituationMatrimoniale;
                u.AdresseCasablanca = kyc.AdresseCasablanca;
                u.NiveauEtude = kyc.NiveauEtude;
                u.Telephone = kyc.Telephone;
                u.ReseauPrincipal = kyc.ReseauPrincipal;
                u.DocIdentiteUrl = kyc.DocIdentiteUrl;
                u.DocDomicileUrl = kyc.DocDomicileUrl;
                u.CguAcceptees = kyc.CguAcceptees;
                u.PendingPremiumUpgrade = true;
                u.Solde -= 10_000L; // 100 DH en centimes
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                _user.Profil.Solde = u.Solde;
                _user.Profil.PendingPremiumUpgrade = true;
                await _notifHist.AddNotificationAsync("Dossier KYC soumis — en attente de validation", "INFO", "KYC");
                _logger.LogInformation("Dossier KYC soumis pour {Email}", PiiMasker.MaskEmail(_user.Profil.Email));
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ADR-001 : soldes en centimes (long)
        public async Task<List<(DateTime Jour, long Solde)>> GetBalanceCurve30DaysAsync()
        {
            var today = DateTime.UtcNow.Date;
            var from  = today.AddDays(-29);

            var txs = await _context.Transactions
                .Where(t => t.UserId == _user.Profil.Id && t.Date >= from)
                .ToListAsync();

            // Net par jour : positif = entrée, négatif = sortie
            var dailyNet = new Dictionary<DateTime, long>();
            foreach (var tx in txs)
            {
                var day = tx.Date.Date;
                dailyNet[day] = dailyNet.GetValueOrDefault(day) + (tx.IsEntree ? tx.Montant : -tx.Montant);
            }

            // Reconstitution vers le passé depuis le solde actuel
            var curve = new long[30];
            curve[29] = _user.Profil.Solde;
            for (int i = 28; i >= 0; i--)
                curve[i] = curve[i + 1] - dailyNet.GetValueOrDefault(from.AddDays(i + 1));

            return Enumerable.Range(0, 30)
                .Select(i => (from.AddDays(i), curve[i]))
                .ToList();
        }

        // --- HISTORIQUE CONNEXIONS ---
        public async Task<List<UserLogin>> GetLoginHistoryAsync(int count = 20)
        {
            return await _context.UserLogins
                .Where(l => l.UserId == _user.Profil.Id)
                .OrderByDescending(l => l.Date)
                .Take(count)
                .ToListAsync();
        }

        // --- PLAFONDS --- ADR-001 : montantCentimes en long
        public void ReinitialiserPlafondsSiNecessaire()
        {
            var now = DateTime.UtcNow;
            var u = _user.Profil;
            if (u.DerniereReinitPlafond.Date != now.Date)
            {
                u.MontantJournalierUtilise = 0L;
                u.DerniereReinitPlafond = now;
            }
            if (u.DerniereReinitPlafond.Month != now.Month || u.DerniereReinitPlafond.Year != now.Year)
            {
                u.MontantMensuelUtilise = 0L;
            }
        }

        public (bool Allow, string Message) VerifierPlafond(long montantCentimes)
        {
            ReinitialiserPlafondsSiNecessaire();
            var u = _user.Profil;
            if (u.MontantJournalierUtilise + montantCentimes > u.PlafondJournalier)
                return (false, $"Plafond journalier dépassé ({u.PlafondJournalier.ToDh()}/jour)");
            if (u.MontantMensuelUtilise + montantCentimes > u.PlafondMensuel)
                return (false, $"Plafond mensuel dépassé ({u.PlafondMensuel.ToDh()}/mois)");
            return (true, "");
        }

        public async Task<(bool Success, string Message)> UpdatePlafondsAsync(long journalierCentimes, long mensuelCentimes)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");
            if (journalierCentimes <= 0 || mensuelCentimes <= 0) return (false, "Les plafonds doivent être positifs");
            if (journalierCentimes > mensuelCentimes) return (false, "Le plafond journalier ne peut pas dépasser le plafond mensuel");
            u.PlafondJournalier = journalierCentimes;
            u.PlafondMensuel = mensuelCentimes;
            await _context.SaveChangesAsync();
            _user.Profil.PlafondJournalier = journalierCentimes;
            _user.Profil.PlafondMensuel = mensuelCentimes;
            await _notifHist.AddNotificationAsync(
                $"Plafonds mis à jour : {journalierCentimes.ToDh()}/jour, {mensuelCentimes.ToDh()}/mois", "SUCCESS", "PLAFOND");
            _logger.LogInformation("Plafonds mis à jour pour {Email}: {J}/{M}",
                PiiMasker.MaskEmail(_user.Profil.Email), journalierCentimes.ToDh(), mensuelCentimes.ToDh());
            return (true, "Plafonds mis à jour");
        }

        // --- BÉNÉFICIAIRES ---
        public async Task<List<Beneficiaire>> GetBeneficiairesAsync()
        {
            return await _context.Beneficiaires
                .Where(b => b.UserId == _user.Profil.Id)
                .OrderByDescending(b => b.DateAjout)
                .ToListAsync();
        }

        public async Task<(bool Success, string Message)> AjouterBeneficiaireAsync(string nom, string email, string? banque, string? rib)
        {
            if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(email))
                return (false, "Nom et email requis");
            var emailLower = email.Trim().ToLower();
            if (await _context.Beneficiaires.AnyAsync(b => b.UserId == _user.Profil.Id && b.Email == emailLower))
                return (false, "Ce bénéficiaire existe déjà");
            _context.Beneficiaires.Add(new Beneficiaire
            {
                UserId = _user.Profil.Id,
                Nom = nom.Trim(),
                Email = emailLower,
                Banque = banque?.Trim(),
                RIB = rib?.Trim()
            });
            await _context.SaveChangesAsync();
            await _notifHist.AddNotificationAsync($"Bénéficiaire ajouté : {nom} ({emailLower})", "SUCCESS", "BENEFICIAIRE");
            _logger.LogInformation("Bénéficiaire {Nom} ({Email}) ajouté par {User}",
                nom, PiiMasker.MaskEmail(emailLower), PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, "Bénéficiaire ajouté");
        }

        public async Task<bool> SupprimerBeneficiaireAsync(int id)
        {
            var b = await _context.Beneficiaires.FirstOrDefaultAsync(x => x.Id == id && x.UserId == _user.Profil.Id);
            if (b == null) return false;
            _context.Beneficiaires.Remove(b);
            await _context.SaveChangesAsync();
            await _notifHist.AddNotificationAsync($"Bénéficiaire #{id} supprimé", "INFO", "BENEFICIAIRE");
            _logger.LogInformation("Bénéficiaire #{Id} supprimé par {User}", id, PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        // --- PRÉFÉRENCES NOTIFICATIONS ---
        public async Task<(bool Success, string Message)> UpdateNotificationPrefsAsync(
            bool connexion, bool virement, bool depot, bool retrait, bool epargne, bool credit, bool promo)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");
            u.NotifConnexion = connexion;
            u.NotifVirement = virement;
            u.NotifDepot = depot;
            u.NotifRetrait = retrait;
            u.NotifEpargne = epargne;
            u.NotifCredit = credit;
            u.NotifPromo = promo;
            await _context.SaveChangesAsync();
            _user.Profil.NotifConnexion = connexion;
            _user.Profil.NotifVirement = virement;
            _user.Profil.NotifDepot = depot;
            _user.Profil.NotifRetrait = retrait;
            _user.Profil.NotifEpargne = epargne;
            _user.Profil.NotifCredit = credit;
            _user.Profil.NotifPromo = promo;
            await _notifHist.AddNotificationAsync("Préférences de notification mises à jour", "SUCCESS", "PARAMETRES");
            _logger.LogInformation("Préférences notifications mises à jour pour {Email}",
                PiiMasker.MaskEmail(_user.Profil.Email));
            return (true, "Préférences enregistrées");
        }

        // --- RÉVOCATION SESSIONS ---
        public async Task<bool> RevokeAllSessionsAsync()
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return false;
            u.SecurityStamp = Guid.NewGuid().ToString();
            await _context.SaveChangesAsync();
            _user.Profil.SecurityStamp = u.SecurityStamp;
            await _notifHist.AddNotificationAsync("Toutes les sessions ont été révoquées", "INFO", "SECURITE");
            _logger.LogWarning("Toutes les sessions révoquées pour {Email}", PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        // --- ADMIN : tout l'historique des connexions ---
        public async Task<List<UserLogin>> GetAllLoginHistoryAsync(int count = 50)
        {
            return await _context.UserLogins
                .OrderByDescending(l => l.Date)
                .Take(count)
                .ToListAsync();
        }
    }
}
