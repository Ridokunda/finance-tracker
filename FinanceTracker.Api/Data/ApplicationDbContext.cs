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
        public DbSet<CategoryLimit> CategoryLimits { get; set; }
        public DbSet<ImportBatch> ImportBatches { get; set; }
        public DbSet<ImportRow> ImportRows { get; set; }
        public DbSet<CategorizationRule> CategorizationRules { get; set; }
        public DbSet<RecurringBill> RecurringBills { get; set; }
        public DbSet<BillPayment> BillPayments { get; set; }
        public DbSet<BudgetWarningSettings> BudgetWarningSettings { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Transaction entity
            modelBuilder.Entity<Transaction>()
                .Property(t => t.Amount)
                .HasPrecision(18, 2); // SQL Server decimal(18,2)
            modelBuilder.Entity<Transaction>().Property(t => t.Date).HasColumnType("date");
            modelBuilder.Entity<Transaction>().HasQueryFilter(t => t.DeletedAt == null);
            modelBuilder.Entity<Transaction>().HasIndex(t => new { t.UserId, t.Date });
            modelBuilder.Entity<Transaction>().HasIndex(t => t.ImportRowId).IsUnique().HasFilter("[ImportRowId] IS NOT NULL");
            modelBuilder.Entity<Transaction>().HasOne<ImportRow>().WithMany().HasForeignKey(t => t.ImportRowId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<MonthlyBudget>().Property(b => b.LimitAmount).HasPrecision(18, 2);
            modelBuilder.Entity<MonthlyBudget>().Property(b => b.Version).IsRowVersion();
            modelBuilder.Entity<MonthlyBudget>().HasIndex(b => new { b.UserId, b.Year, b.Month }).IsUnique();
            modelBuilder.Entity<MonthlyBudget>().ToTable(t => t.HasCheckConstraint("CK_Budget_Month", "[Month] BETWEEN 1 AND 12 AND [Year] BETWEEN 1900 AND 9998"));
            modelBuilder.Entity<CategoryLimit>().Property(c => c.Category).HasMaxLength(100).UseCollation("SQL_Latin1_General_CP1_CI_AS");
            modelBuilder.Entity<CategoryLimit>().Property(c => c.LimitAmount).HasPrecision(18, 2);
            modelBuilder.Entity<CategoryLimit>().HasIndex(c => new { c.MonthlyBudgetId, c.Category }).IsUnique();
            modelBuilder.Entity<CategoryLimit>().ToTable(t => t.HasCheckConstraint("CK_CategoryLimit_Amount", "[LimitAmount] >= 0"));
            modelBuilder.Entity<ImportBatch>().HasOne<AppUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ImportBatch>().Property(b => b.Version).IsRowVersion();
            modelBuilder.Entity<ImportBatch>().HasIndex(b => new { b.UserId, b.ExpiresAt });
            modelBuilder.Entity<ImportRow>().Property(r => r.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<ImportRow>().Property(r => r.Date).HasColumnType("date");
            modelBuilder.Entity<CategorizationRule>().Property(r => r.Pattern).HasMaxLength(200).UseCollation("SQL_Latin1_General_CP1_CI_AS");
            modelBuilder.Entity<CategorizationRule>().Property(r => r.Category).HasMaxLength(100);
            modelBuilder.Entity<CategorizationRule>().HasIndex(r => new { r.UserId, r.Pattern }).IsUnique();
            modelBuilder.Entity<CategorizationRule>().HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<RecurringBill>().Property(b => b.Name).HasMaxLength(120);
            modelBuilder.Entity<RecurringBill>().Property(b => b.MatchText).HasMaxLength(200);
            modelBuilder.Entity<RecurringBill>().Property(b => b.Category).HasMaxLength(100);
            modelBuilder.Entity<RecurringBill>().Property(b => b.Frequency).HasMaxLength(20);
            modelBuilder.Entity<RecurringBill>().Property(b => b.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<RecurringBill>().Property(b => b.FirstDueDate).HasColumnType("date");
            modelBuilder.Entity<RecurringBill>().Property(b => b.EndDate).HasColumnType("date");
            modelBuilder.Entity<RecurringBill>().HasIndex(b => b.UserId);
            modelBuilder.Entity<RecurringBill>().HasOne<AppUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<RecurringBill>().ToTable(t => t.HasCheckConstraint("CK_RecurringBill_Amount", "[Amount] > 0"));
            modelBuilder.Entity<BillPayment>().Property(p => p.DueDate).HasColumnType("date");
            modelBuilder.Entity<BillPayment>().HasIndex(p => new { p.RecurringBillId, p.DueDate }).IsUnique();
            modelBuilder.Entity<BillPayment>().HasIndex(p => p.TransactionId).IsUnique();
            modelBuilder.Entity<BillPayment>().HasOne<RecurringBill>().WithMany().HasForeignKey(p => p.RecurringBillId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<BillPayment>().HasOne(p => p.Transaction).WithMany().HasForeignKey(p => p.TransactionId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<BillPayment>().HasQueryFilter(p => p.Transaction.DeletedAt == null);
            modelBuilder.Entity<BudgetWarningSettings>().HasKey(s => s.UserId);
            modelBuilder.Entity<BudgetWarningSettings>().HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<BudgetWarningSettings>().ToTable(t => t.HasCheckConstraint("CK_BudgetWarning_Threshold", "[ThresholdPercent] BETWEEN 1 AND 99"));
        }
    }
}

