using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;

namespace ADN_pay.Services
{
    public class NotificationHistoryService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;

        public NotificationHistoryService(BankDbContext context, UserContext user)
        {
            _context = context;
            _user = user;
        }

        public async Task AddNotificationAsync(string message, string type = "INFO", string categorie = "GENERAL")
        {
            _context.NotificationHistories.Add(new NotificationHistory
            {
                UserId = _user.Profil.Id,
                Message = message,
                Type = type,
                Categorie = categorie
            });
            await _context.SaveChangesAsync();
        }

        public async Task AddNotificationForUserAsync(int userId, string message, string type = "INFO", string categorie = "GENERAL")
        {
            _context.NotificationHistories.Add(new NotificationHistory
            {
                UserId = userId,
                Message = message,
                Type = type,
                Categorie = categorie
            });
            await _context.SaveChangesAsync();
        }

        public async Task<List<NotificationHistory>> GetNotificationsAsync(int count = 20)
        {
            return await _context.NotificationHistories
                .Where(n => n.UserId == _user.Profil.Id)
                .OrderByDescending(n => n.Date)
                .Take(count)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync()
        {
            return await _context.NotificationHistories
                .CountAsync(n => n.UserId == _user.Profil.Id && !n.Lu);
        }

        public async Task MarkAsReadAsync(int id)
        {
            var n = await _context.NotificationHistories.FindAsync(id);
            if (n != null) { n.Lu = true; await _context.SaveChangesAsync(); }
        }

        public async Task MarkAllAsReadAsync()
        {
            var unread = await _context.NotificationHistories
                .Where(n => n.UserId == _user.Profil.Id && !n.Lu)
                .ToListAsync();
            foreach (var n in unread) n.Lu = true;
            await _context.SaveChangesAsync();
        }

        public async Task<int> DeleteOldNotificationsAsync(int keepDays = 30)
        {
            var cutoff = DateTime.UtcNow.AddDays(-keepDays);
            var old = await _context.NotificationHistories
                .Where(n => n.UserId == _user.Profil.Id && n.Date < cutoff)
                .ToListAsync();
            _context.NotificationHistories.RemoveRange(old);
            await _context.SaveChangesAsync();
            return old.Count;
        }
    }
}
