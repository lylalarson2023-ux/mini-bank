using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;

namespace ADN_pay.Services
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
            // Solde minimum 100 DH = 10 000 centimes
            return await Task.FromResult(u.Statut != UserStatus.STANDARD && u.Solde > 10_000L);
        }

        // ADR-001 : montantCentimes en long. TauxAnnuel reste decimal (pourcentage).
        public async Task<bool> SoumettreDemandeCredit(long montantCentimes, string categorie, int dureeMois)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return false;

            var tauxAnnuel = categorie switch
            {
                "MICRO" => 0.05m,
                "PERSONNEL" => 0.10m,
                "BUSINESS" => 0.15m,
                _ => 0.10m
            };
            if (u.Statut == UserStatus.VIP) tauxAnnuel -= 0.03m;
            else if (u.Statut == UserStatus.PREMIUM) tauxAnnuel -= 0.015m;

            var demande = new CreditRequest
            {
                UserId = _user.Profil.Id,
                Montant = montantCentimes,
                Categorie = categorie,
                DureeMois = dureeMois,
                TauxAnnuel = tauxAnnuel,
                Statut = "EN_ATTENTE",
                DateDemande = DateTime.UtcNow
            };
            _context.CreditRequests.Add(demande);

            u.PendingCreditRequest = true;
            u.PendingCreditAmount = montantCentimes;
            u.PendingCreditMotif = $"Catégorie: {categorie}, Durée: {dureeMois} mois";
            await _context.SaveChangesAsync();
            _logger.LogInformation("Demande crédit soumise : {Montant} ({Categorie}) pour {Email}",
                montantCentimes.ToDh(), categorie, PiiMasker.MaskEmail(_user.Profil.Email));
            return true;
        }

        public async Task<List<CreditRequest>> GetDemandesEnAttenteAsync()
        {
            return await _context.CreditRequests
                .Include(c => c.User)
                .Where(c => c.Statut == "EN_ATTENTE")
                .OrderByDescending(c => c.DateDemande)
                .ToListAsync();
        }

        // Demandes de l'utilisateur connecté (suivi de statut côté client)
        public async Task<List<CreditRequest>> GetMesDemandesAsync()
        {
            return await _context.CreditRequests
                .Where(c => c.UserId == _user.Profil.Id)
                .OrderByDescending(c => c.DateDemande)
                .ToListAsync();
        }

        public async Task<bool> RejeterDemandeAsync(int demandeId, string motif)
        {
            var demande = await _context.CreditRequests.FindAsync(demandeId);
            if (demande == null) return false;

            demande.Statut = "REJETE";
            demande.MotifRejet = motif;

            var u = await _context.UserProfiles.FindAsync(demande.UserId);
            if (u != null)
            {
                u.PendingCreditRequest = false;
                u.PendingCreditAmount = 0L;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Demande crédit #{Id} rejetée : {Motif}", demandeId, motif);
            return true;
        }
    }
}
