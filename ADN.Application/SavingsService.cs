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
        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;
        private readonly ILogger<SavingsService> _logger;
        private readonly NotificationHistoryService _notifHist;

        public SavingsService(IDbContextFactory<BankDbContext> factory, UserContext user, ILogger<SavingsService> logger,
            NotificationHistoryService notifHist)
        {
            _factory = factory;
            _user = user;
            _logger = logger;
            _notifHist = notifHist;
        }

        public async Task<List<SavingsPocket>> GetPocketsAsync()
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.SavingsPockets
                .Where(p => p.UserId == _user.Profil.Id)
                .ToListAsync();
        }

        // ADR-001 : montants en centimes (long)
        public async Task<bool> CreerPocheEpargne(string obj, long montantInitialCentimes, DateTime fin,
            long montantCibleCentimes = 0L, bool tuteurVisible = false)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var u = await ctx.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null || u.Solde < montantInitialCentimes) return false;

                if (montantCibleCentimes <= 0L) montantCibleCentimes = montantInitialCentimes * 2;

                u.Solde -= montantInitialCentimes;
                ctx.SavingsPockets.Add(new SavingsPocket
                {
                    UserId = _user.Profil.Id,
                    Objectif = obj,
                    TuteurVisible = tuteurVisible,
                    Cible = fin,
                    MontantCible = montantCibleCentimes,
                    MontantActuel = montantInitialCentimes
                });
                // Trace le débit du compte courant vers l'épargne dans l'historique.
                ctx.Transactions.Add(new Transaction
                {
                    UserId = _user.Profil.Id,
                    Type = "ÉPARGNE",
                    Montant = montantInitialCentimes,
                    SoldeApres = u.Solde,
                    Libelle = $"Épargne — {obj}",
                    Motif = "Transfert vers poche d'épargne"
                });
                var result = await ctx.SaveChangesAsync() > 0;
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
            await using var ctx = await _factory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var p = await ctx.SavingsPockets.FindAsync(id);
                if (p == null || p.UserId != _user.Profil.Id) return false;
                var u = await ctx.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null) return false;
                var montantRecupere = p.MontantActuel;
                var objectif = p.Objectif;
                u.Solde += montantRecupere;
                ctx.SavingsPockets.Remove(p);
                // Trace le retour de l'épargne vers le compte courant (entrée).
                ctx.Transactions.Add(new Transaction
                {
                    UserId = _user.Profil.Id,
                    Type = "RETOUR_ÉPARGNE",
                    Montant = montantRecupere,
                    SoldeApres = u.Solde,
                    Libelle = $"Retour d'épargne — {objectif}",
                    Motif = "Récupération de poche d'épargne"
                });
                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                _user.Profil.Solde = u.Solde;
                await _notifHist.AddNotificationAsync(
                    $"Poche d'épargne récupérée : {montantRecupere.ToDh()} reversés", "INFO", "EPARGNE");
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
            await using var ctx = await _factory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var p = await ctx.SavingsPockets.FindAsync(id);
                if (p == null || p.UserId != _user.Profil.Id) return false;
                var u = await ctx.UserProfiles.FindAsync(_user.Profil.Id);
                if (u == null || u.Solde < montantCentimes)
                {
                    _logger.LogWarning("Boost poche #{Id} refusé : solde insuffisant pour {Email}",
                        id, PiiMasker.MaskEmail(_user.Profil.Email));
                    return false;
                }
                u.Solde -= montantCentimes;
                p.MontantActuel += montantCentimes;
                _user.Profil.Solde = u.Solde;
                // Trace le débit du compte courant vers l'épargne dans l'historique.
                ctx.Transactions.Add(new Transaction
                {
                    UserId = _user.Profil.Id,
                    Type = "ÉPARGNE",
                    Montant = montantCentimes,
                    SoldeApres = u.Solde,
                    Libelle = $"Boost épargne — {p.Objectif}",
                    Motif = "Versement vers poche d'épargne"
                });
                await ctx.SaveChangesAsync();
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

        // Boost effectué PAR LE TUTEUR sur la poche d'un étudiant : débite le tuteur, crédite la poche.
        public async Task<(bool Success, string Message)> BoosterPocheCommeTuteurAsync(int pocketId, long montantCentimes)
        {
            if (montantCentimes <= 0) return (false, "Montant invalide");

            await using var ctx = await _factory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var p = await ctx.SavingsPockets.FindAsync(pocketId);
                if (p == null) return (false, "Poche introuvable");

                // L'étudiant propriétaire doit avoir autorisé ce tuteur…
                var student = await ctx.UserProfiles.FindAsync(p.UserId);
                if (student == null
                    || !student.TuteurAutorise
                    || !string.Equals(student.TuteurEmail, _user.Profil.Email, StringComparison.OrdinalIgnoreCase))
                    return (false, "Vous n'êtes pas le tuteur autorisé de cet étudiant");

                // …et la poche doit être visible par le tuteur.
                if (!(p.TuteurVisible || p.Objectif.Contains("TUTEUR", StringComparison.OrdinalIgnoreCase)))
                    return (false, "Cette poche n'est pas partagée avec vous");

                var tuteur = await ctx.UserProfiles.FindAsync(_user.Profil.Id);
                if (tuteur == null || tuteur.Solde < montantCentimes)
                    return (false, "Solde insuffisant sur votre compte");

                tuteur.Solde -= montantCentimes;
                p.MontantActuel += montantCentimes;
                _user.Profil.Solde = tuteur.Solde;

                // Trace le boost dans l'historique de l'étudiant (visible côté étudiant ET tuteur).
                ctx.Transactions.Add(new Transaction
                {
                    UserId = student.Id,
                    // Type neutre : le versement va dans la POCHE, le solde courant de
                    // l'étudiant ne bouge pas → ne doit pas peser sur la courbe de solde.
                    Type = "BOOST_TUTEUR",
                    Montant = montantCentimes,
                    Frais = 0L,
                    SoldeApres = student.Solde,
                    Libelle = $"Boost tuteur — {p.Objectif}",
                    Motif = $"Versement de votre tuteur ({_user.Profil.Email})"
                });

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();

                await _notifHist.AddNotificationForUserAsync(student.Id,
                    $"Votre tuteur a boosté « {p.Objectif} » de {montantCentimes.ToDh()}", "SUCCESS", "EPARGNE");

                _logger.LogInformation("Tuteur {Tuteur} a boosté la poche #{Id} de {Montant} (étudiant {Student})",
                    PiiMasker.MaskEmail(_user.Profil.Email), pocketId, montantCentimes.ToDh(), PiiMasker.MaskEmail(student.Email));

                return (true, $"Poche boostée de {montantCentimes.ToDh()}");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<TuteurPocketView>> GetPocketsForTuteurAsync()
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var students = await ctx.UserProfiles
                .Where(u => u.TuteurEmail == _user.Profil.Email && u.TuteurAutorise)
                .ToListAsync();
            var result = new List<TuteurPocketView>();
            foreach (var s in students)
            {
                var pockets = await ctx.SavingsPockets
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
            await using var ctx = await _factory.CreateDbContextAsync();
            var studentIds = await ctx.UserProfiles
                .Where(u => u.TuteurEmail == _user.Profil.Email && u.TuteurAutorise)
                .Select(u => u.Id)
                .ToListAsync();

            if (!studentIds.Any()) return 0L;

            // Somme des boosts réellement versés par ce tuteur ce mois-ci (tracés en Transaction).
            return await ctx.Transactions
                .Where(t => studentIds.Contains(t.UserId)
                    && t.Date >= debutMois
                    && t.Libelle.StartsWith("Boost tuteur"))
                .SumAsync(t => t.Montant);
        }

        public async Task<List<Transaction>> GetRecentActivityForTuteurAsync(int count = 20)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var studentIds = await ctx.UserProfiles
                .Where(u => u.TuteurEmail == _user.Profil.Email && u.TuteurAutorise)
                .Select(u => u.Id)
                .ToListAsync();

            if (!studentIds.Any()) return new();

            return await ctx.Transactions
                .Where(t => studentIds.Contains(t.UserId))
                .OrderByDescending(t => t.Date)
                .Take(count)
                .ToListAsync();
        }
    }
}
