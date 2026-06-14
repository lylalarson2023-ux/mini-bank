using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Shared.Infrastructure;

namespace ADN_pay.Services
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

        // ADR-001 : montants en centimes (long)
        public async Task<bool> CreerPocheEpargne(string obj, long montantInitialCentimes, DateTime fin,
            long montantCibleCentimes = 0L, bool tuteurVisible = false)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null || u.Solde < montantInitialCentimes) return false;

                if (montantCibleCentimes <= 0L) montantCibleCentimes = montantInitialCentimes * 2;

                u.Solde -= montantInitialCentimes;
                _context.SavingsPockets.Add(new SavingsPocket
                {
                    UserId = _user.Profil.Id,
                    Objectif = obj,
                    TuteurVisible = tuteurVisible,
                    Cible = fin,
                    MontantCible = montantCibleCentimes,
                    MontantActuel = montantInitialCentimes
                });
                var result = await _context.SaveChangesAsync() > 0;
                await tx.CommitAsync();
                if (result)
                {
                    _user.Profil.Solde = u.Solde;
                    await _notifHist.AddNotificationAsync(
                        $"Poche d'épargne créée : {obj} ({montantInitialCentimes.ToDh()})", "SUCCESS", "EPARGNE");
                    _logger.LogInformation("Poche d'épargne créée : {Objectif} avec {Montant} pour {Email}",
                        obj, montantInitialCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email));
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
                if (u == null) return false;
                u.Solde += p.MontantActuel;
                _context.SavingsPockets.Remove(p);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                await _notifHist.AddNotificationAsync(
                    $"Poche d'épargne récupérée : {p.MontantActuel.ToDh()} reversés", "INFO", "EPARGNE");
                _logger.LogInformation("Poche d'épargne #{Id} cassée par {Email}",
                    id, PiiMasker.MaskEmail(_user.Profil.Email));
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> BoosterPocheAsync(int id, long montantCentimes)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var p = await _context.SavingsPockets.FindAsync(id);
                if (p == null || p.UserId != _user.Profil.Id) return false;
                var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null || u.Solde < montantCentimes)
                {
                    _logger.LogWarning("Boost poche #{Id} refusé : solde insuffisant pour {Email}",
                        id, PiiMasker.MaskEmail(_user.Profil.Email));
                    return false;
                }
                u.Solde -= montantCentimes;
                p.MontantActuel += montantCentimes;
                _user.Profil.Solde = u.Solde;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                await _notifHist.AddNotificationAsync(
                    $"Poche boostée de {montantCentimes.ToDh()}", "SUCCESS", "EPARGNE");
                _logger.LogInformation("Poche #{Id} boostée de {Montant} par {Email}",
                    id, montantCentimes.ToDh(), PiiMasker.MaskEmail(_user.Profil.Email));
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
                    .Where(p => p.UserId == s.Id && (p.TuteurVisible || p.Objectif.Contains("TUTEUR")))
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

        public async Task<long> GetTotalInvestiThisMonthAsync()
        {
            var debutMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var studentIds = await _context.UserProfiles
                .Where(u => u.TuteurEmail == _user.Profil.Email && u.TuteurAutorise)
                .Select(u => u.Id)
                .ToListAsync();

            if (!studentIds.Any()) return 0L;

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
