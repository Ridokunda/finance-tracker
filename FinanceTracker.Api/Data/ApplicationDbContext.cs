using FinanceTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<AppUser> Users { get; set; }
        public DbSet<RevokedToken> RevokedTokens { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<MonthlyBudget> MonthlyBudgets { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Transaction entity
            modelBuilder.Entity<Transaction>()
                .Property(t => t.Amount)
                .HasPrecision(18, 2); // SQL Server decimal(18,2)
        }
    }
}

