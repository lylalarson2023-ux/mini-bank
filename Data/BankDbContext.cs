using ADN_pay.Models;
using Microsoft.EntityFrameworkCore;

namespace ADN_pay.Data
{
    public class BankDbContext : DbContext
    {
        public BankDbContext(DbContextOptions<BankDbContext> options) : base(options) { }

        // Tables de l'application
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<SavingsPocket> SavingsPockets { get; set; }
        public DbSet<UserLogin> UserLogins { get; set; }
        public DbSet<Beneficiaire> Beneficiaires { get; set; }
        public DbSet<CreditRequest> CreditRequests { get; set; }
        
        // Table pour l'historique des tâches administrateur
        public DbSet<AdminLog> AdminLogs { get; set; }
        public DbSet<NotificationHistory> NotificationHistories { get; set; }
        public DbSet<PawaPayDeposit> PawaPayDeposits { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuration pour SQLite gérant les décimaux
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.GetProperties()
                    .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?));

                foreach (var property in properties)
                {
                    property.SetPrecision(18);
                    property.SetScale(2);
                }
            }
        }
    }
}