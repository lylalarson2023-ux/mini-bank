using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;

namespace ADN_pay.Services
{
    public class NotificationHistoryService
    {
        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;

        public NotificationHistoryService(IDbContextFactory<BankDbContext> factory, UserContext user)
        {
            _factory = factory;
            _user = user;
        }

        public async Task AddNotificationAsync(string message, string type = "INFO", string categorie = "GENERAL")
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            ctx.NotificationHistories.Add(new NotificationHistory
            {
                UserId = _user.Profil.Id,
                Message = message,
                Type = type,
                Categorie = categorie
            });
            await ctx.SaveChangesAsync();
        }

        public async Task AddNotificationForUserAsync(int userId, string message, string type = "INFO", string categorie = "GENERAL")
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            ctx.NotificationHistories.Add(new NotificationHistory
            {
                UserId = userId,
                Message = message,
                Type = type,
                Categorie = categorie
            });
            await ctx.SaveChangesAsync();
        }

        public async Task<List<NotificationHistory>> GetNotificationsAsync(int count = 20)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.NotificationHistories
                .Where(n => n.UserId == _user.Profil.Id)
                .OrderByDescending(n => n.Date)
                .Take(count)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync()
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.NotificationHistories
                .CountAsync(n => n.UserId == _user.Profil.Id && !n.Lu);
        }

        public async Task MarkAsReadAsync(int id)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var n = await ctx.NotificationHistories.FindAsync(id);
            if (n != null) { n.Lu = true; await ctx.SaveChangesAsync(); }
        }

        public async Task MarkAllAsReadAsync()
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var unread = await ctx.NotificationHistories
                .Where(n => n.UserId == _user.Profil.Id && !n.Lu)
                .ToListAsync();
            foreach (var n in unread) n.Lu = true;
            await ctx.SaveChangesAsync();
        }

        public async Task<int> DeleteOldNotificationsAsync(int keepDays = 30)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var cutoff = DateTime.UtcNow.AddDays(-keepDays);
            var old = await ctx.NotificationHistories
                .Where(n => n.UserId == _user.Profil.Id && n.Date < cutoff)
                .ToListAsync();
            ctx.NotificationHistories.RemoveRange(old);
            await ctx.SaveChangesAsync();
            return old.Count;
        }
    }
}
