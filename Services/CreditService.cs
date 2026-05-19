using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;

namespace MBANK_ETUDIANT.Services
{
    public class CreditService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<CreditService> _logger;

        public CreditService(BankDbContext context, UserContext user, ILogger<CreditService> logger)
        {
            _context = context;
            _user = user;
            _logger = logger;
        }

        public async Task<bool> VerifierEligibiliteCredit(int userId)
        {
            var u = await _context.UserProfiles.FindAsync(userId);
            if (u == null) return false;
            return await Task.FromResult(
                u.Statut != UserStatus.STANDARD && u.Solde > 100);
        }

        public async Task<bool> SoumettreDemandeCredit(decimal montant, string categorie, int dureeMois)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return false;
            u.PendingCreditRequest = true;
            u.PendingCreditAmount = montant;
            u.PendingCreditMotif = $"Catégorie: {categorie}, Durée: {dureeMois} mois";
            await _context.SaveChangesAsync();
            _logger.LogInformation("Demande crédit soumise : {Montant} DH ({Categorie}) pour {Email}",
                montant, categorie, _user.Profil.Email);
            return true;
        }
    }
}
