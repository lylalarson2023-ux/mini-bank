using MBANK_ETUDIANT.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace MBANK_ETUDIANT.Services
{
    public class BankService
    {
        private readonly AuthService _auth;
        private readonly AccountService _account;
        private readonly SavingsService _savings;
        private readonly CreditService _credit;
        private readonly AdminService _admin;
        private readonly FileService _file;
        private readonly UserContext _user;

        public BankService(
            AuthService auth, AccountService account, SavingsService savings,
            CreditService credit, AdminService admin, FileService file, UserContext user)
        {
            _auth = auth;
            _account = account;
            _savings = savings;
            _credit = credit;
            _admin = admin;
            _file = file;
            _user = user;
        }

        // --- ÉTAT PARTAGÉ ---
        public UserProfile Profil => _user.Profil;
        public bool EstConnecte => _user.EstConnecte;

        // --- AUTH ---
        public Task InitializeAsync(int userId) => _auth.InitializeAsync(userId);
        public Task<bool> SeConnecter(string email, string password) => _auth.SeConnecter(email, password);
        public Task<bool> VerifierSessionExistante() => _auth.VerifierSessionExistante();
        public void Deconnexion() => _auth.Deconnexion();
        public Task<bool> CreerNouveauCompte(UserProfile u, string password) => _auth.CreerNouveauCompte(u, password);

        // --- COMPTE ---
        public Task<bool> ExecuterOperationAsync(decimal montant, string motif, string type = "VIREMENT")
            => _account.ExecuterOperationAsync(montant, motif, type);
        public Task<List<Transaction>> GetHistoriqueAsync() => _account.GetHistoriqueAsync();
        public Task<decimal> GetBalanceAsync() => _account.GetBalanceAsync();
        public Task<List<Transaction>> GetRecentTransactionsAsync(int count) => _account.GetRecentTransactionsAsync(count);
        public Task<bool> EffectuerVirementAsync(string emailDestinataire, decimal montant, string motif) => _account.EffectuerVirementAsync(emailDestinataire, montant, motif);
        public Task<bool> DefinirTuteur(string email) => _account.DefinirTuteur(email);
        public Task<bool> RevoquerTuteur() => _account.RevoquerTuteur();
        public Task<bool> SoumettreDossierKYC(UserProfile kyc) => _account.SoumettreDossierKYC(kyc);

        // --- ÉPARGNE ---
        public Task<List<SavingsPocket>> GetPocketsAsync() => _savings.GetPocketsAsync();
        public Task<bool> CreerPocheEpargne(string obj, decimal cible, DateTime fin) => _savings.CreerPocheEpargne(obj, cible, fin);
        public Task<bool> CasserPocheEpargne(int id) => _savings.CasserPocheEpargne(id);
        public Task<bool> BoosterPocheAsync(int id, decimal mnt) => _savings.BoosterPocheAsync(id, mnt);
        public Task<List<TuteurPocketView>> GetPocketsForTuteurAsync() => _savings.GetPocketsForTuteurAsync();

        // --- CRÉDIT ---
        public Task<bool> VerifierEligibiliteCredit(int userId) => _credit.VerifierEligibiliteCredit(userId);
        public Task<bool> SoumettreDemandeCredit(decimal montant, string categorie, int dureeMois)
            => _credit.SoumettreDemandeCredit(montant, categorie, dureeMois);

        // --- ADMIN ---
        public Task<List<UserProfile>> GetDossiersEnAttenteAsync() => _admin.GetDossiersEnAttenteAsync();
        public Task<decimal> GetTotalBankBalanceAsync() => _admin.GetTotalBankBalanceAsync();
        public Task<List<AdminLog>> GetAdminLogsAsync() => _admin.GetAdminLogsAsync();
        public Task<bool> ApprouverPremium(int id) => _admin.ApprouverPremium(id);
        public Task<bool> ApprouverCredit(int id, decimal montantForce = 0) => _admin.ApprouverCredit(id, montantForce);
        public Task<bool> AdminDepot(int userId, decimal montant) => _admin.AdminDepot(userId, montant);

        // --- FICHIERS ---
        public Task<string> EnregistrerFichierSurDisque(IBrowserFile f) => _file.EnregistrerFichierSurDisque(f);
    }
}
