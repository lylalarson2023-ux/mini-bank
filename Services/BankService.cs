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
        private readonly NotificationHistoryService _notifHist;

        public BankService(
            AuthService auth, AccountService account, SavingsService savings,
            CreditService credit, AdminService admin, FileService file, UserContext user,
            NotificationHistoryService notifHist)
        {
            _auth = auth;
            _account = account;
            _savings = savings;
            _credit = credit;
            _admin = admin;
            _file = file;
            _user = user;
            _notifHist = notifHist;
        }

        // --- ÉTAT PARTAGÉ ---
        public UserProfile Profil => _user.Profil;
        public bool EstConnecte => _user.EstConnecte;

        // --- AUTH ---
        public Task InitializeAsync(int userId) => _auth.InitializeAsync(userId);
        public Task<bool> SeConnecter(string email, string password) => _auth.SeConnecter(email, password);
        public Task<bool> VerifierSessionExistante() => _auth.VerifierSessionExistante();
        public void Deconnexion() => _auth.Deconnexion();
        public Task<(bool Success, string Message)> CreerNouveauCompte(UserProfile u, string password) => _auth.CreerNouveauCompte(u, password);

        // --- COMPTE ---
        public Task<bool> ExecuterOperationAsync(decimal montant, string motif, string type = "VIREMENT")
            => _account.ExecuterOperationAsync(montant, motif, type);
        public Task<List<Transaction>> GetHistoriqueAsync() => _account.GetHistoriqueAsync();
        public Task<decimal> GetBalanceAsync() => _account.GetBalanceAsync();
        public Task<List<Transaction>> GetRecentTransactionsAsync(int count) => _account.GetRecentTransactionsAsync(count);
        public Task<(decimal RevenusMois, decimal DepensesMois, decimal TotalEpargne, List<(DateTime Jour, decimal Entrees, decimal Sorties)> DailyBreakdown)> GetDashboardStatsAsync()
            => _account.GetDashboardStatsAsync();
        public Task<bool> EffectuerVirementAsync(string emailDestinataire, decimal montant, string motif) => _account.EffectuerVirementAsync(emailDestinataire, montant, motif);
        public Task<bool> DefinirTuteur(string email) => _account.DefinirTuteur(email);
        public Task<bool> RevoquerTuteur() => _account.RevoquerTuteur();
        public Task<bool> SoumettreDossierKYC(UserProfile kyc) => _account.SoumettreDossierKYC(kyc);

        // --- PARAMÈTRES COMPTE ---
        public Task<(bool Success, string Message)> UpdateProfileAsync(string nom, string prenom, string telephone, string email)
            => _account.UpdateProfileAsync(nom, prenom, telephone, email);
        public Task<(bool Success, string Message)> ChangerMotDePasseAsync(string currentPassword, string newPassword)
            => _account.ChangerMotDePasseAsync(currentPassword, newPassword);
        public Task<string> ExportPersonalDataAsync() => _account.ExportPersonalDataAsync();
        public Task<(bool Success, string Message)> SupprimerCompteAsync() => _account.SupprimerCompteAsync();

        // --- ÉPARGNE ---
        public Task<List<SavingsPocket>> GetPocketsAsync() => _savings.GetPocketsAsync();
        public Task<bool> CreerPocheEpargne(string obj, decimal montantInitial, DateTime fin, decimal montantCible = 0) => _savings.CreerPocheEpargne(obj, montantInitial, fin, montantCible);
        public Task<bool> CasserPocheEpargne(int id) => _savings.CasserPocheEpargne(id);
        public Task<bool> BoosterPocheAsync(int id, decimal mnt) => _savings.BoosterPocheAsync(id, mnt);
        public Task<List<TuteurPocketView>> GetPocketsForTuteurAsync() => _savings.GetPocketsForTuteurAsync();
        public Task<decimal> GetTotalInvestiThisMonthAsync() => _savings.GetTotalInvestiThisMonthAsync();
        public Task<List<Transaction>> GetRecentActivityForTuteurAsync(int count = 20) => _savings.GetRecentActivityForTuteurAsync(count);

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

        // --- CONNEXIONS ---
        public Task<List<UserLogin>> GetLoginHistoryAsync(int count = 20) => _account.GetLoginHistoryAsync(count);
        public Task<bool> RevokeAllSessionsAsync() => _account.RevokeAllSessionsAsync();

        // --- PLAFONDS ---
        public (bool Allow, string Message) VerifierPlafond(decimal montant) => _account.VerifierPlafond(montant);
        public Task<(bool Success, string Message)> UpdatePlafondsAsync(decimal journalier, decimal mensuel)
            => _account.UpdatePlafondsAsync(journalier, mensuel);

        // --- BÉNÉFICIAIRES ---
        public Task<List<Beneficiaire>> GetBeneficiairesAsync() => _account.GetBeneficiairesAsync();
        public Task<(bool Success, string Message)> AjouterBeneficiaireAsync(string nom, string email, string? banque, string? rib)
            => _account.AjouterBeneficiaireAsync(nom, email, banque, rib);
        public Task<bool> SupprimerBeneficiaireAsync(int id) => _account.SupprimerBeneficiaireAsync(id);

        // --- NOTIFICATIONS ---
        public Task<(bool Success, string Message)> UpdateNotificationPrefsAsync(
            bool connexion, bool virement, bool depot, bool retrait, bool epargne, bool credit, bool promo)
            => _account.UpdateNotificationPrefsAsync(connexion, virement, depot, retrait, epargne, credit, promo);

        // --- ADMIN ---
        public Task<List<UserLogin>> GetAllLoginHistoryAsync(int count = 50) => _account.GetAllLoginHistoryAsync(count);

        // --- ADMIN KYC ---
        public Task<UserProfile?> GetUserByIdAsync(int userId) => _admin.GetUserByIdAsync(userId);
        public Task<(bool Success, string Message)> RejeterDossierKycAsync(int userId) => _admin.RejeterDossierKycAsync(userId);
        public Task<List<UserProfile>> GetHistoriqueDossiersAsync() => _admin.GetHistoriqueDossiersAsync();

        // --- ADMIN TUTEURS ---
        public Task<List<UserProfile>> GetStudentsSansTuteurAsync() => _admin.GetStudentsSansTuteurAsync();
        public Task<List<(UserProfile Student, UserProfile? Tuteur)>> GetRelationsTuteurAsync() => _admin.GetRelationsTuteurAsync();
        public Task<(bool Success, string Message)> AssignerTuteurAsync(int studentId, string tuteurEmail) => _admin.AssignerTuteurAsync(studentId, tuteurEmail);
        public Task<(bool Success, string Message)> RevoquerTuteurParAdminAsync(int studentId) => _admin.RevoquerTuteurParAdminAsync(studentId);

        // --- NOTIFICATIONS HISTORIQUE ---
        public Task<List<NotificationHistory>> GetNotificationsAsync(int count = 20) => _notifHist.GetNotificationsAsync(count);
        public Task<int> GetUnreadCountAsync() => _notifHist.GetUnreadCountAsync();
        public Task MarkAsReadAsync(int id) => _notifHist.MarkAsReadAsync(id);
        public Task MarkAllAsReadAsync() => _notifHist.MarkAllAsReadAsync();
        public Task<int> DeleteOldNotificationsAsync(int keepDays = 30) => _notifHist.DeleteOldNotificationsAsync(keepDays);

        // --- FICHIERS ---
        public Task<string> EnregistrerFichierSurDisque(IBrowserFile f) => _file.EnregistrerFichierSurDisque(f);
    }
}
