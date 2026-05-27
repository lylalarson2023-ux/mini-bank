using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;

namespace MBANK_ETUDIANT.Services
{
    public class AccountService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<AccountService> _logger;
        private readonly NotificationHistoryService _notifHist;

        public AccountService(BankDbContext context, UserContext user, ILogger<AccountService> logger,
            NotificationHistoryService notifHist)
        {
            _context = context;
            _user = user;
            _logger = logger;
            _notifHist = notifHist;
        }

        public async Task<bool> ExecuterOperationAsync(decimal montant, string motif, string type = "VIREMENT")
        {
            if (montant <= 0 || ((type == "RETRAIT" || type == "VIREMENT") && _user.Profil.Solde < montant))
            {
                _logger.LogWarning("{Type} refusé : montant={Montant}, solde={Solde}", type, montant, _user.Profil.Solde);
                return false;
            }
            if (type == "RETRAIT" || type == "VIREMENT")
            {
                var (allow, msg) = VerifierPlafond(montant);
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
                user.Solde -= montant;
                user.MontantJournalierUtilise += montant;
                user.MontantMensuelUtilise += montant;
            }
            else user.Solde += montant;

            user.NombreTransactions++;
            _context.Transactions.Add(new Transaction
            {
                UserId = user.Id,
                Montant = montant,
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
            var sens = type == "DÉPÔT" ? "de" : "de";
            await _notifHist.AddNotificationAsync(
                $"{label} {sens} {montant:N2} DH — {motif}", "SUCCESS", type);

            _logger.LogInformation("{Type} de {Montant} DH effectué par {Email}", type, montant, _user.Profil.Email);
            return true;
        }

        public async Task<List<Transaction>> GetHistoriqueAsync()
            => await _context.Transactions
                .Where(t => t.UserId == _user.Profil.Id)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

        public async Task<decimal> GetBalanceAsync()
        {
            var account = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            return account?.Solde ?? 0;
        }

        public async Task<List<Transaction>> GetRecentTransactionsAsync(int count)
            => await _context.Transactions
                .Where(t => t.UserId == _user.Profil.Id)
                .OrderByDescending(t => t.Date)
                .Take(count)
                .ToListAsync();

        // --- DASHBOARD STATS ---
        public async Task<(decimal RevenusMois, decimal DepensesMois, decimal TotalEpargne, List<(DateTime Jour, decimal Entrees, decimal Sorties)> DailyBreakdown)> GetDashboardStatsAsync()
        {
            var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var transactionsMois = await _context.Transactions
                .Where(t => t.UserId == _user.Profil.Id && t.Date >= debutMois)
                .ToListAsync();

            var revenus = transactionsMois
                .Where(t => t.Type is "DÉPÔT" or "RÉCEPTION" or "CRÉDIT")
                .Sum(t => t.Montant);
            var depenses = transactionsMois
                .Where(t => t.Type is "RETRAIT" or "VIREMENT")
                .Sum(t => t.Montant);

            var totalEpargne = await _context.SavingsPockets
                .Where(p => p.UserId == _user.Profil.Id)
                .SumAsync(p => p.MontantActuel);

            // Daily breakdown for chart (last 7 days)
            var septJours = DateTime.UtcNow.Date.AddDays(-6);
            var daily = Enumerable.Range(0, 7).Select(offset =>
            {
                var day = septJours.AddDays(offset);
                var dayTx = transactionsMois.Where(t => t.Date.Date == day).ToList();
                return (
                    Jour: day,
                    Entrees: dayTx.Where(t => t.Type is "DÉPÔT" or "RÉCEPTION" or "CRÉDIT").Sum(t => t.Montant),
                    Sorties: dayTx.Where(t => t.Type is "RETRAIT" or "VIREMENT").Sum(t => t.Montant)
                );
            }).ToList();

            return (revenus, depenses, totalEpargne, daily);
        }

        // --- TUTEUR ---
        public async Task<bool> EffectuerVirementAsync(string emailDestinataire, decimal montant, string motif)
        {
            var (allow, msg) = VerifierPlafond(montant);
            if (!allow) return false;

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var sender = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (sender == null || sender.Solde < montant) return false;

                var recipient = await _context.UserProfiles.FirstOrDefaultAsync(u => u.Email == emailDestinataire.Trim().ToLower());
                if (recipient == null) return false;

                sender.Solde -= montant;
                sender.MontantJournalierUtilise += montant;
                sender.MontantMensuelUtilise += montant;
                recipient.Solde += montant;
                sender.NombreTransactions++;
                recipient.NombreTransactions++;

                _context.Transactions.Add(new Transaction
                {
                    UserId = sender.Id,
                    Montant = montant,
                    Type = "VIREMENT",
                    Motif = motif,
                    SoldeApres = sender.Solde,
                    Libelle = $"Virement vers {recipient.Email}",
                    Date = DateTime.UtcNow
                });
                _context.Transactions.Add(new Transaction
                {
                    UserId = recipient.Id,
                    Montant = montant,
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
                _logger.LogInformation("Virement de {Montant} DH de {Sender} vers {Recipient}", montant, sender.Email, recipient.Email);

                // Notify recipient
                await _notifHist.AddNotificationForUserAsync(recipient.Id,
                    $"Virement reçu de {sender.Email} : {montant:N2} DH", "SUCCESS", "VIREMENT");

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
            _logger.LogInformation("Tuteur {TuteurEmail} autorisé pour {Email}", email, _user.Profil.Email);
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
            _logger.LogInformation("Tuteur révoqué pour {Email}", _user.Profil.Email);
            return true;
        }

        // --- PARAMÈTRES COMPTE ---
        public async Task<(bool Success, string Message)> UpdateProfileAsync(string nom, string prenom, string telephone, string email)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");

            var emailLower = email.Trim().ToLower();
            if (emailLower != _user.Profil.Email && await _context.UserProfiles.AnyAsync(x => x.Email == emailLower))
                return (false, "Cet email est déjà utilisé");

            u.Nom = nom?.Trim() ?? "";
            u.Prenom = prenom?.Trim() ?? "";
            u.Telephone = telephone?.Trim() ?? "";
            u.Email = emailLower;

            await _context.SaveChangesAsync();

            _user.Profil.Nom = u.Nom;
            _user.Profil.Prenom = u.Prenom;
            _user.Profil.Telephone = u.Telephone;
            _user.Profil.Email = u.Email;

            await _notifHist.AddNotificationAsync("Profil mis à jour", "SUCCESS", "PROFIL");
            _logger.LogInformation("Profil mis à jour pour {Email}", _user.Profil.Email);
            return (true, "Profil mis à jour avec succès");
        }

        public async Task<(bool Success, string Message)> ChangerMotDePasseAsync(string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return (false, "Le nouveau mot de passe est requis");

            if (newPassword.Length < 8)
                return (false, "Le mot de passe doit contenir au moins 8 caractères");

            if (!newPassword.Any(char.IsUpper))
                return (false, "Le mot de passe doit contenir au moins une majuscule");
            

            if (!newPassword.Any(char.IsDigit))
                return (false, "Le mot de passe doit contenir au moins un chiffre");

            if (!newPassword.Any(c => !char.IsLetterOrDigit(c)))
                return (false, "Le mot de passe doit contenir au moins un caractère spécial (!@#$%^&*)");

            var commonPasswords = new HashSet<string> { "Password123!", "12345678", "Azerty123", "Motdepasse1", "Admin123!", "Test1234!" };
            if (commonPasswords.Contains(newPassword))
                return (false, "Ce mot de passe est trop commun. Veuillez en choisir un plus sécurisé.");

            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, u.MotDePasseHash))
                return (false, "Mot de passe actuel incorrect");

            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _context.SaveChangesAsync();

            await _notifHist.AddNotificationAsync("Mot de passe changé", "SUCCESS", "PROFIL");
            _logger.LogInformation("Mot de passe changé pour {Email}", _user.Profil.Email);
            return (true, "Mot de passe changé avec succès");
        }

        // --- EXPORT DES DONNEES (RGPD / Loi 09-08) ---
        public async Task<string> ExportPersonalDataAsync()
        {
            var u = await _context.UserProfiles
                .Include(x => x.Transactions)
                .Include(x => x.SavingsPockets)
                .FirstOrDefaultAsync(x => x.Id == _user.Profil.Id);

            if (u == null) return "";

            var export = new System.Text.StringBuilder();
            export.AppendLine("=== EXPORT DES DONNEES PERSONNELLES - MBANK ===");
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
            export.AppendLine($"Solde : {u.Solde} DH");
            export.AppendLine($"Dette : {u.Dette} DH");
            export.AppendLine($"Statut : {u.Statut}");
            export.AppendLine($"Date d'inscription : {u.DateInscription:yyyy-MM-dd HH:mm}");
            export.AppendLine($"Nombre de transactions : {u.NombreTransactions}");
            export.AppendLine();
            export.AppendLine("--- HISTORIQUE DES TRANSACTIONS ---");
            foreach (var t in u.Transactions.OrderByDescending(x => x.Date))
            {
                export.AppendLine($"[{t.Date:yyyy-MM-dd HH:mm}] {t.Type} - {t.Motif} - {t.Montant} DH");
            }
            export.AppendLine();
            export.AppendLine("--- POCKETS D'EPARGNE ---");
            foreach (var p in u.SavingsPockets)
            {
                export.AppendLine($"{p.Objectif} : {p.MontantActuel} DH (fin : {p.Cible:yyyy-MM-dd})");
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
        public async Task<(bool Success, string Message)> SupprimerCompteAsync()
        {
            var u = await _context.UserProfiles
                .Include(x => x.Transactions)
                .Include(x => x.SavingsPockets)
                .FirstOrDefaultAsync(x => x.Id == _user.Profil.Id);

            if (u == null) return (false, "Utilisateur introuvable");

            if (u.Solde > 0)
                return (false, "Veuillez transférer ou retirer votre solde avant de supprimer votre compte");

            _logger.LogWarning("SUPPRESSION COMPTE : {Email} (Id={Id})", u.Email, u.Id);

            _context.Transactions.RemoveRange(u.Transactions);
            _context.SavingsPockets.RemoveRange(u.SavingsPockets);
            _context.UserProfiles.Remove(u);
            await _context.SaveChangesAsync();

            _user.Profil = new();
            _user.EstConnecte = false;

            _logger.LogInformation("Compte supprimé : {Email}", u.Email);
            return (true, "Votre compte a été supprimé avec succès. Vous allez être redirigé.");
        }

        // --- KYC ---
        public async Task<bool> SoumettreDossierKYC(UserProfile kyc)
        {
            if (_user.Profil.Solde < 100m) return false;
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
                u.Solde -= 100m;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                _user.Profil.Solde = u.Solde;
                await _notifHist.AddNotificationAsync("Dossier KYC soumis — en attente de validation", "INFO", "KYC");
                _logger.LogInformation("Dossier KYC soumis pour {Email}", _user.Profil.Email);
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
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

        // --- PLAFONDS ---
        public void ReinitialiserPlafondsSiNecessaire()
        {
            var now = DateTime.UtcNow;
            var u = _user.Profil;
            if (u.DerniereReinitPlafond.Date != now.Date)
            {
                u.MontantJournalierUtilise = 0;
                u.DerniereReinitPlafond = now;
            }
            if (u.DerniereReinitPlafond.Month != now.Month || u.DerniereReinitPlafond.Year != now.Year)
            {
                u.MontantMensuelUtilise = 0;
            }
        }

        public (bool Allow, string Message) VerifierPlafond(decimal montant)
        {
            ReinitialiserPlafondsSiNecessaire();
            var u = _user.Profil;
            if (u.MontantJournalierUtilise + montant > u.PlafondJournalier)
                return (false, $"Plafond journalier dépassé ({u.PlafondJournalier:N0} DH/jour)");
            if (u.MontantMensuelUtilise + montant > u.PlafondMensuel)
                return (false, $"Plafond mensuel dépassé ({u.PlafondMensuel:N0} DH/mois)");
            return (true, "");
        }

        public async Task<(bool Success, string Message)> UpdatePlafondsAsync(decimal journalier, decimal mensuel)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");
            if (journalier <= 0 || mensuel <= 0) return (false, "Les plafonds doivent être positifs");
            if (journalier > mensuel) return (false, "Le plafond journalier ne peut pas dépasser le plafond mensuel");
            u.PlafondJournalier = journalier;
            u.PlafondMensuel = mensuel;
            await _context.SaveChangesAsync();
            _user.Profil.PlafondJournalier = journalier;
            _user.Profil.PlafondMensuel = mensuel;
            await _notifHist.AddNotificationAsync($"Plafonds mis à jour : {journalier:N0} DH/jour, {mensuel:N0} DH/mois", "SUCCESS", "PLAFOND");
            _logger.LogInformation("Plafonds mis à jour pour {Email}: {J}/{M}", _user.Profil.Email, journalier, mensuel);
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
            _logger.LogInformation("Bénéficiaire {Nom} ({Email}) ajouté par {User}", nom, emailLower, _user.Profil.Email);
            return (true, "Bénéficiaire ajouté");
        }

        public async Task<bool> SupprimerBeneficiaireAsync(int id)
        {
            var b = await _context.Beneficiaires.FirstOrDefaultAsync(x => x.Id == id && x.UserId == _user.Profil.Id);
            if (b == null) return false;
            _context.Beneficiaires.Remove(b);
            await _context.SaveChangesAsync();
            await _notifHist.AddNotificationAsync($"Bénéficiaire #{id} supprimé", "INFO", "BENEFICIAIRE");
            _logger.LogInformation("Bénéficiaire #{Id} supprimé par {User}", id, _user.Profil.Email);
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
            _logger.LogInformation("Préférences notifications mises à jour pour {Email}", _user.Profil.Email);
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
            _logger.LogWarning("Toutes les sessions révoquées pour {Email}", _user.Profil.Email);
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
