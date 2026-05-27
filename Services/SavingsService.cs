using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;

namespace MBANK_ETUDIANT.Services
{
    public class TuteurPocketView
    {
        public SavingsPocket Pocket { get; set; } = null!;
        public string StudentEmail { get; set; } = "";
        public string StudentName { get; set; } = "";
    }

    public class SavingsService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<SavingsService> _logger;
        private readonly NotificationHistoryService _notifHist;

        public SavingsService(BankDbContext context, UserContext user, ILogger<SavingsService> logger,
            NotificationHistoryService notifHist)
        {
            _context = context;
            _user = user;
            _logger = logger;
            _notifHist = notifHist;
        }

        public async Task<List<SavingsPocket>> GetPocketsAsync()
            => await _context.SavingsPockets
                .Where(p => p.UserId == _user.Profil.Id)
                .ToListAsync();

        public async Task<bool> CreerPocheEpargne(string obj, decimal montantInitial, DateTime fin, decimal montantCible = 0)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null || u.Solde < montantInitial) return false;

                if (montantCible <= 0) montantCible = montantInitial * 2;

                u.Solde -= montantInitial;
                _context.SavingsPockets.Add(new SavingsPocket
                {
                    UserId = _user.Profil.Id,
                    Objectif = obj,
                    Cible = fin,
                    MontantCible = montantCible,
                    MontantActuel = montantInitial
                });
                var result = await _context.SaveChangesAsync() > 0;
                await tx.CommitAsync();
                if (result)
                {
                    _user.Profil.Solde = u.Solde;
                    await _notifHist.AddNotificationAsync($"Poche d'épargne créée : {obj} ({montantInitial:N2} DH)", "SUCCESS", "EPARGNE");
                    _logger.LogInformation("Poche d'épargne créée : {Objectif} avec {Montant} DH pour {Email}", obj, montantInitial, _user.Profil.Email);
                }
                return result;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> CasserPocheEpargne(int id)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var p = await _context.SavingsPockets.FindAsync(id);
                if (p == null || p.UserId != _user.Profil.Id) return false;
                var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (u != null) u.Solde += p.MontantActuel;
                _context.SavingsPockets.Remove(p);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                await _notifHist.AddNotificationAsync($"Poche d'épargne récupérée : {p.MontantActuel:N2} DH reversés", "INFO", "EPARGNE");
                _logger.LogInformation("Poche d'épargne #{Id} cassée par {Email}", id, _user.Profil.Email);
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> BoosterPocheAsync(int id, decimal mnt)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var p = await _context.SavingsPockets.FindAsync(id);
                if (p == null || p.UserId != _user.Profil.Id) return false;
                var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null || u.Solde < mnt)
                {
                    _logger.LogWarning("Boost poche #{Id} refusé : solde insuffisant pour {Email}", id, _user.Profil.Email);
                    return false;
                }
                u.Solde -= mnt;
                p.MontantActuel += mnt;
                _user.Profil.Solde = u.Solde;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                await _notifHist.AddNotificationAsync($"Poche boostée de {mnt:N2} DH", "SUCCESS", "EPARGNE");
                _logger.LogInformation("Poche #{Id} boostée de {Montant} DH par {Email}", id, mnt, _user.Profil.Email);
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<TuteurPocketView>> GetPocketsForTuteurAsync()
        {
            var students = await _context.UserProfiles
                .Where(u => u.TuteurEmail == _user.Profil.Email && u.TuteurAutorise)
                .ToListAsync();
            var result = new List<TuteurPocketView>();
            foreach (var s in students)
            {
                var pockets = await _context.SavingsPockets
                    .Where(p => p.UserId == s.Id && p.Objectif.Contains("TUTEUR"))
                    .ToListAsync();
                foreach (var p in pockets)
                {
                    result.Add(new TuteurPocketView
                    {
                        Pocket = p,
                        StudentEmail = s.Email,
                        StudentName = s.GetFullName()
                    });
                }
            }
            return result;
        }

        public async Task<decimal> GetTotalInvestiThisMonthAsync()
        {
            var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var studentIds = await _context.UserProfiles
                .Where(u => u.TuteurEmail == _user.Profil.Email && u.TuteurAutorise)
                .Select(u => u.Id)
                .ToListAsync();

            if (!studentIds.Any()) return 0;

            return await _context.SavingsPockets
                .Where(p => studentIds.Contains(p.UserId) && p.DateCreation >= debutMois)
                .SumAsync(p => p.MontantActuel);
        }

        public async Task<List<Transaction>> GetRecentActivityForTuteurAsync(int count = 20)
        {
            var studentIds = await _context.UserProfiles
                .Where(u => u.TuteurEmail == _user.Profil.Email && u.TuteurAutorise)
                .Select(u => u.Id)
                .ToListAsync();

            if (!studentIds.Any()) return new();

            return await _context.Transactions
                .Where(t => studentIds.Contains(t.UserId))
                .OrderByDescending(t => t.Date)
                .Take(count)
                .ToListAsync();
        }
    }
}
