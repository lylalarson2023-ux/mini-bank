using ADN_pay.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace ADN_pay.Services
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
        private readonly TwoFactorService _twoFactor;

        public BankService(
            AuthService auth, AccountService account, SavingsService savings,
            CreditService credit, AdminService admin, FileService file, UserContext user,
            NotificationHistoryService notifHist, TwoFactorService twoFactor)
        {
            _auth = auth;
            _account = account;
            _savings = savings;
            _credit = credit;
            _admin = admin;
            _file = file;
            _user = user;
            _notifHist = notifHist;
            _twoFactor = twoFactor;
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

        // --- COMPTE --- ADR-001 : montants en centimes (long)
        public Task<bool> ExecuterOperationAsync(long montantCentimes, string motif, string type = "VIREMENT")
            => _account.ExecuterOperationAsync(montantCentimes, motif, type);
        public Task<List<Transaction>> GetHistoriqueAsync() => _account.GetHistoriqueAsync();
        public Task<long> GetBalanceAsync() => _account.GetBalanceAsync();
        public Task<List<Transaction>> GetRecentTransactionsAsync(int count) => _account.GetRecentTransactionsAsync(count);
        public Task<(long RevenusMois, long DepensesMois, long TotalEpargne,
            List<(DateTime Jour, long Entrees, long Sorties)> DailyBreakdown)> GetDashboardStatsAsync()
            => _account.GetDashboardStatsAsync();
        public Task<List<(DateTime Jour, long Solde)>> GetBalanceCurve30DaysAsync()
            => _account.GetBalanceCurve30DaysAsync();
        public Task<bool> EffectuerVirementAsync(string emailDestinataire, long montantCentimes, string motif)
            => _account.EffectuerVirementAsync(emailDestinataire, montantCentimes, motif);
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

        // --- ÉPARGNE --- ADR-001 : montants en centimes (long)
        public Task<List<SavingsPocket>> GetPocketsAsync() => _savings.GetPocketsAsync();
        public Task<bool> CreerPocheEpargne(string obj, long montantInitialCentimes, DateTime fin,
            long montantCibleCentimes = 0L, bool tuteurVisible = false)
            => _savings.CreerPocheEpargne(obj, montantInitialCentimes, fin, montantCibleCentimes, tuteurVisible);
        public Task<bool> CasserPocheEpargne(int id) => _savings.CasserPocheEpargne(id);
        public Task<bool> BoosterPocheAsync(int id, long montantCentimes) => _savings.BoosterPocheAsync(id, montantCentimes);
        public Task<List<TuteurPocketView>> GetPocketsForTuteurAsync() => _savings.GetPocketsForTuteurAsync();
        public Task<long> GetTotalInvestiThisMonthAsync() => _savings.GetTotalInvestiThisMonthAsync();
        public Task<List<Transaction>> GetRecentActivityForTuteurAsync(int count = 20) => _savings.GetRecentActivityForTuteurAsync(count);

        // --- CRÉDIT --- ADR-001 : montant en centimes (long)
        public Task<bool> VerifierEligibiliteCredit(int userId) => _credit.VerifierEligibiliteCredit(userId);
        public Task<bool> SoumettreDemandeCredit(long montantCentimes, string categorie, int dureeMois)
            => _credit.SoumettreDemandeCredit(montantCentimes, categorie, dureeMois);

        // --- ADMIN --- ADR-001 : montants en centimes (long)
        public Task<List<UserProfile>> GetDossiersEnAttenteAsync() => _admin.GetDossiersEnAttenteAsync();
        public Task<long> GetTotalBankBalanceAsync() => _admin.GetTotalBankBalanceAsync();
        public Task<List<AdminLog>> GetAdminLogsAsync() => _admin.GetAdminLogsAsync();
        public Task<bool> ApprouverPremium(int id) => _admin.ApprouverPremium(id);
        public Task<bool> ApprouverCredit(int id, long montantForceCentimes = 0L) => _admin.ApprouverCredit(id, montantForceCentimes);
        public Task<bool> RejeterCreditAsync(int userId, string motif) => _admin.RejeterCreditAsync(userId, motif);
        public Task<bool> AdminDepot(int userId, long montantCentimes) => _admin.AdminDepot(userId, montantCentimes);

        // --- CONNEXIONS ---
        public Task<List<UserLogin>> GetLoginHistoryAsync(int count = 20) => _account.GetLoginHistoryAsync(count);
        public Task<bool> RevokeAllSessionsAsync() => _account.RevokeAllSessionsAsync();

        // --- PLAFONDS --- ADR-001 : montants en centimes (long)
        public (bool Allow, string Message) VerifierPlafond(long montantCentimes) => _account.VerifierPlafond(montantCentimes);
        public Task<(bool Success, string Message)> UpdatePlafondsAsync(long journalierCentimes, long mensuelCentimes)
            => _account.UpdatePlafondsAsync(journalierCentimes, mensuelCentimes);

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
        public Task<(bool Success, string Message)> RejeterDossierKycAsync(int userId, string? motif = null) => _admin.RejeterDossierKycAsync(userId, motif);
        public Task<List<UserProfile>> GetHistoriqueDossiersAsync() => _admin.GetHistoriqueDossiersAsync();

        // --- ADMIN TUTEURS ---
        public Task<List<UserProfile>> GetStudentsSansTuteurAsync() => _admin.GetStudentsSansTuteurAsync();
        public Task<List<(UserProfile Student, UserProfile? Tuteur)>> GetRelationsTuteurAsync() => _admin.GetRelationsTuteurAsync();
        public Task<(bool Success, string Message)> AssignerTuteurAsync(int studentId, string tuteurEmail) => _admin.AssignerTuteurAsync(studentId, tuteurEmail);
        public Task<(bool Success, string Message)> RevoquerTuteurParAdminAsync(int studentId) => _admin.RevoquerTuteurParAdminAsync(studentId);

        // --- NOTIFICATIONS HISTORIQUE ---
        public Task<List<NotificationHistory>> GetNotificationsAsync(int count = 20) => _notifHist.GetNotificationsAsync(count);
        public async Task<int> GetUnreadCountAsync()
        {
            NotifCount = await _notifHist.GetUnreadCountAsync();
            return NotifCount;
        }
        public async Task MarkAsReadAsync(int id)
        {
            await _notifHist.MarkAsReadAsync(id);
            NotifCount = await _notifHist.GetUnreadCountAsync();
            NotifCountChanged?.Invoke();
        }
        public async Task MarkAllAsReadAsync()
        {
            await _notifHist.MarkAllAsReadAsync();
            NotifCount = 0;
            NotifCountChanged?.Invoke();
        }
        public async Task<int> DeleteOldNotificationsAsync(int keepDays = 30)
        {
            var count = await _notifHist.DeleteOldNotificationsAsync(keepDays);
            NotifCount = await _notifHist.GetUnreadCountAsync();
            NotifCountChanged?.Invoke();
            return count;
        }

        public int NotifCount { get; private set; }
        public event Action? NotifCountChanged;

        // --- FICHIERS ---
        public Task<string> EnregistrerFichierSurDisque(IBrowserFile f) => _file.EnregistrerFichierSurDisque(f);

        // --- 2FA ---
        public string GenerateTwoFactorSecret() => _twoFactor.GenerateSecret();
        public string GenerateTwoFactorQrUri(string secret, string email) => _twoFactor.GenerateQrUri(secret, email);
        public Task<(bool, string)> EnableTwoFactorAsync(string code) => _twoFactor.EnableAsync(code);
        public Task DisableTwoFactorAsync() => _twoFactor.DisableAsync();
        public bool VerifyTwoFactorCode(string secret, string code) => _twoFactor.VerifyCodeWithWindow(secret, code);
        public bool IsTwoFactorRequired => _twoFactor.IsTwoFactorRequired;
        public Task<bool> UserHasTwoFactorAsync(int userId) => _twoFactor.UserHasTwoFactorAsync(userId);
    }
}
