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

        public AccountService(BankDbContext context, UserContext user, ILogger<AccountService> logger)
        {
            _context = context;
            _user = user;
            _logger = logger;
        }

        public async Task<bool> ExecuterOperationAsync(decimal montant, string motif, string type = "VIREMENT")
        {
            if (montant <= 0 || ((type == "RETRAIT" || type == "VIREMENT") && _user.Profil.Solde < montant))
            {
                _logger.LogWarning("{Type} refusé : montant={Montant}, solde={Solde}", type, montant, _user.Profil.Solde);
                return false;
            }
            var user = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (user == null) return false;

            if (type == "RETRAIT" || type == "VIREMENT") user.Solde -= montant;
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
                Date = DateTime.Now
            });
            await _context.SaveChangesAsync();
            _user.Profil.Solde = user.Solde;
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

        // --- TUTEUR ---
        public async Task<bool> EffectuerVirementAsync(string emailDestinataire, decimal montant, string motif)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var sender = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (sender == null || sender.Solde < montant) return false;

                var recipient = await _context.UserProfiles.FirstOrDefaultAsync(u => u.Email == emailDestinataire.Trim().ToLower());
                if (recipient == null) return false;

                sender.Solde -= montant;
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
                    Date = DateTime.Now
                });
                _context.Transactions.Add(new Transaction
                {
                    UserId = recipient.Id,
                    Montant = montant,
                    Type = "RÉCEPTION",
                    Motif = $"Virement de {sender.Email}",
                    SoldeApres = recipient.Solde,
                    Libelle = $"Virement reçu de {sender.Email}",
                    Date = DateTime.Now
                });

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                _user.Profil.Solde = sender.Solde;
                _logger.LogInformation("Virement de {Montant} DH de {Sender} vers {Recipient}", montant, sender.Email, recipient.Email);
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
            _logger.LogInformation("Tuteur révoqué pour {Email}", _user.Profil.Email);
            return true;
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
                _logger.LogInformation("Dossier KYC soumis pour {Email}", _user.Profil.Email);
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
