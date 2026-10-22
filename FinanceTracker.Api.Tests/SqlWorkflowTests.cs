using System.Security.Claims;
using System.Text;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
namespace FinanceTracker.Api.Tests;
public class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINANCE_TEST_SQL"))) Skip = "Set FINANCE_TEST_SQL to run SQL Server integration tests."; }
}
public class SqlDatabase : IAsyncLifetime
{
    public string? Connection { get; private set; }
    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("FINANCE_TEST_SQL");
        if (string.IsNullOrWhiteSpace(source)) return;
        Connection = new SqlConnectionStringBuilder(source) { InitialCatalog = "FinanceTrackerTests_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var db = Open();
        await db.GetService<IMigrator>().MigrateAsync("20251120182205_AddMonthlyBudget");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Users (Username, PasswordHash) VALUES ('legacy-migration', 'test'); INSERT INTO MonthlyBudgets (UserId, Year, Month, LimitAmount) SELECT Id, 2026, 8, 5000 FROM Users WHERE Username = 'legacy-migration'; INSERT INTO MonthlyBudgets (UserId, Year, Month, LimitAmount) SELECT Id, 2026, 9, -200 FROM Users WHERE Username = 'legacy-migration';");
        await db.Database.MigrateAsync();
    }
    public ApplicationDbContext Open() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Connection!).Options);
    public async Task DisposeAsync()
    {
        if (Connection == null) return;
        var name = new SqlConnectionStringBuilder(Connection).InitialCatalog;
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^FinanceTrackerTests_[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test database name.");
        await using var db = Open(); await db.Database.EnsureDeletedAsync();
    }
}
public class SqlWorkflowTests(SqlDatabase database) : IClassFixture<SqlDatabase>
{
    private class Offline : IClassifierClient { public Task<string?> Suggest(string description, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null); }
    private static T AsUser<T>(T controller, int id) where T : ControllerBase
    {
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", id.ToString())], "Tests")) } };
        return controller;
    }
    private static async Task<int> User(ApplicationDbContext db)
    {
        var user = new AppUser { Username = Guid.NewGuid().ToString("N"), PasswordHash = "test" }; db.Users.Add(user); await db.SaveChangesAsync(); return user.Id;
    }
    private static StatementController Statements(ApplicationDbContext db, int user) => AsUser(new StatementController(db, new StatementParser(), new Offline(), new MonthlyAnalytics(db)), user);
    private static async Task<ImportBatch> Preview(ApplicationDbContext db, int user, string csv = "Date,Description,Amount\n2026-09-01,Checkers,-1200.00\n")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        Assert.IsType<OkObjectResult>(await Statements(db, user).Preview(new FormFile(stream, 0, stream.Length, "file", "statement.csv")));
        return await db.ImportBatches.Include(b => b.Rows).OrderByDescending(b => b.CreatedAt).FirstAsync(b => b.UserId == user);
    }
    private static StatementController.ConfirmRequest Decisions(ImportBatch batch, bool keep = false) => new(batch.Rows.Select(r => new StatementController.RowDecision(r.Id, r.Error == null, "Groceries", keep)).ToList());

    [SqlFact]
    public async Task MigrationPreservesHistoricalLimitsAndRequiresOneTimeReconfirmation()
    {
        await using var db = database.Open();
        var user = await db.Users.SingleAsync(u => u.Username == "legacy-migration");
        var budgets = await db.MonthlyBudgets.Where(b => b.UserId == user.Id).OrderBy(b => b.Month).ToListAsync();
        Assert.Equal(5000, budgets[0].LimitAmount); Assert.Equal(-200, budgets[1].LimitAmount);
        Assert.All(budgets, b => Assert.True(b.NeedsReconfirmation));
        var controller = AsUser(new BudgetController(db, new MonthlyAnalytics(db)), user.Id);
        Assert.IsType<OkObjectResult>(await controller.SetBudget(new() { Year = 2026, Month = 9, LimitAmount = 5000 }));
        db.ChangeTracker.Clear();
        Assert.False((await db.MonthlyBudgets.SingleAsync(b => b.UserId == user.Id && b.Month == 9)).NeedsReconfirmation);
        Assert.True((await db.MonthlyBudgets.SingleAsync(b => b.UserId == user.Id && b.Month == 8)).NeedsReconfirmation);
    }
    [SqlFact]
    public async Task PreviewAndIdempotentConfirmationPreserveLimitDuringClassifierOutage()
    {
        await using var db = database.Open(); var user = await User(db);
        db.MonthlyBudgets.Add(new MonthlyBudget { UserId = user, Year = 2026, Month = 9, LimitAmount = 5000 }); await db.SaveChangesAsync();
        var batch = await Preview(db, user);
        Assert.Empty(await db.Transactions.Where(t => t.UserId == user).ToListAsync());
        Assert.Contains("unavailable", batch.NoticesJson); Assert.Equal("Uncategorized", batch.Rows[0].Category);
        Assert.IsType<OkObjectResult>(await Statements(db, user).Confirm(batch.Id, Decisions(batch)));
        Assert.IsType<OkObjectResult>(await Statements(db, user).Confirm(batch.Id, Decisions(batch)));
        Assert.Single(await db.Transactions.Where(t => t.UserId == user).ToListAsync());
        var summary = await new MonthlyAnalytics(db).Build(user, new DateTime(2026, 9, 1));
        Assert.Equal(5000, summary.Budget); Assert.Equal(1200, summary.Spent); Assert.Equal(3800, summary.Remaining);
        db.Transactions.Add(new Transaction { UserId = user, Date = new DateTime(2026, 9, 1), Amount = -1200, Description = "Duplicate provenance", ImportRowId = batch.Rows[0].Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [SqlFact]
    public async Task InvalidDecisionSavesNothingAndDuplicatesRequireExplicitRetention()
    {
        await using var db = database.Open(); var user = await User(db);
        var batch = await Preview(db, user, "Date,Description,Amount\n2026-09-01,Shop,-10\n2026-09-01,  shop  ,-10\n2026-09-01,Bad,abc\n");
        Assert.True(batch.Rows[1].PossibleDuplicate); Assert.False(batch.Rows[1].Included);
        var invalid = new StatementController.ConfirmRequest(batch.Rows.Select(r => new StatementController.RowDecision(r.Id, true, "Food", true)).ToList());
        Assert.IsType<BadRequestObjectResult>(await Statements(db, user).Confirm(batch.Id, invalid));
        Assert.Empty(await db.Transactions.Where(t => t.UserId == user).ToListAsync());
        Assert.IsType<ConflictObjectResult>(await Statements(db, user).Confirm(batch.Id, Decisions(batch)));
        Assert.IsType<OkObjectResult>(await Statements(db, user).Confirm(batch.Id, Decisions(batch, true)));
        Assert.Equal(2, await db.Transactions.CountAsync(t => t.UserId == user));
        var repeated = await Preview(db, user, "Date,Description,Amount\n2026-09-01,SHOP,-10\n");
        Assert.True(repeated.Rows[0].PossibleDuplicate); Assert.False(repeated.Rows[0].Included);
    }
    [SqlFact]
    public async Task OwnershipExpiryEditingAndMonthBoundaries()
    {
        await using var db = database.Open(); var owner = await User(db); var other = await User(db);
        var batch = await Preview(db, owner);
        Assert.IsType<NotFoundResult>(await Statements(db, other).Get(batch.Id));
        Assert.IsType<NotFoundResult>(await Statements(db, other).Confirm(batch.Id, Decisions(batch)));
        batch.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        Assert.Equal(410, Assert.IsType<ObjectResult>(await Statements(db, owner).Confirm(batch.Id, Decisions(batch))).StatusCode);
        var transactions = AsUser(new TransactionController(db, new Offline()), owner);
        Assert.IsType<OkObjectResult>(transactions.Create(new() { Date = new DateTime(2026, 9, 30), Description = "Last day", Amount = -50, Category = "food" }));
        var transaction = await db.Transactions.SingleAsync(t => t.UserId == owner);
        Assert.IsType<NotFoundObjectResult>(AsUser(new TransactionController(db, new Offline()), other).Update(transaction.Id, new() { Description = "Attack", Amount = -999 }));
        Assert.IsType<OkObjectResult>(transactions.Update(transaction.Id, new() { Date = new DateTime(2026, 10, 1), Description = "Moved", Amount = -100, Category = "transport" }));
        var analytics = new MonthlyAnalytics(db);
        Assert.Equal(0, (await analytics.Build(owner, new DateTime(2026, 9, 1))).Spent);
        var october = await analytics.Build(owner, new DateTime(2026, 10, 1));
        Assert.Equal(100, october.Spent); Assert.Equal("Transport", Assert.Single(october.Categories).Category);
        Assert.False(october.HasBudget); Assert.Equal(6, october.Trends.Count);
        Assert.Equal(0, (await analytics.Build(other, new DateTime(2026, 10, 1))).Spent);
    }
    [SqlFact]
    public async Task BudgetAllocationConstraintsAndCopyAreUserScoped()
    {
        await using var db = database.Open(); var user = await User(db); var other = await User(db);
        var controller = AsUser(new BudgetController(db, new MonthlyAnalytics(db)), user);
        Assert.IsType<BadRequestObjectResult>(await controller.SetBudget(new() { Year = 2026, Month = 9, LimitAmount = 100, CategoryLimits = [new("Food", 101)] }));
        Assert.IsType<OkObjectResult>(await controller.SetBudget(new() { Year = 2026, Month = 9, LimitAmount = 5000, CategoryLimits = [new("groceries", 1500)] }));
        Assert.IsType<OkObjectResult>(await controller.CopyPrevious(new(2026, 10)));
        Assert.Equal(1500, (await new MonthlyAnalytics(db).Build(user, new DateTime(2026, 10, 1))).Categories[0].Limit);
        Assert.False((await new MonthlyAnalytics(db).Build(other, new DateTime(2026, 10, 1))).HasBudget);
        db.MonthlyBudgets.Add(new MonthlyBudget { UserId = user, Year = 2026, Month = 9, LimitAmount = 5 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var budget = await db.MonthlyBudgets.SingleAsync(b => b.UserId == user && b.Month == 9);
        db.CategoryLimits.Add(new CategoryLimit { MonthlyBudgetId = budget.Id, Category = "GROCERIES", LimitAmount = 10 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [SqlFact]
    public async Task DatabaseFailureRollsBackTransactionsAndConfirmation()
    {
        await using var db = database.Open(); var user = await User(db);
        await db.Database.ExecuteSqlRawAsync("CREATE OR ALTER TRIGGER RejectTestImport ON Transactions AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Description = 'FORCE_SQL_FAILURE') THROW 51001, 'Forced test failure', 1; END;");
        var batch = await Preview(db, user, "Date,Description,Amount\n2026-09-01,Valid,-10\n2026-09-02,FORCE_SQL_FAILURE,-20\n");
        Assert.IsType<ConflictObjectResult>(await Statements(db, user).Confirm(batch.Id, Decisions(batch)));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Transactions.Where(t => t.UserId == user).ToListAsync());
        Assert.Null((await db.ImportBatches.SingleAsync(b => b.Id == batch.Id)).ConfirmedAt);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectTestImport");
    }
    [SqlFact]
    public async Task ConcurrentConfirmationInsertsEachRowOnce()
    {
        await using var setup = database.Open(); var user = await User(setup); var batch = await Preview(setup, user);
        await using var first = database.Open(); await using var second = database.Open();
        var results = await Task.WhenAll(Statements(first, user).Confirm(batch.Id, Decisions(batch)), Statements(second, user).Confirm(batch.Id, Decisions(batch)));
        Assert.All(results, result => Assert.IsType<OkObjectResult>(result));
        Assert.Equal(1, await setup.Transactions.CountAsync(t => t.UserId == user));
    }

    [SqlFact]
    public async Task RecoveryBulkEditingAndRulesPersistWithImportProvenance()
    {
        await using var db = database.Open(); var user = await User(db); var other = await User(db);
        var ruleController = AsUser(new CategoryRulesController(db), user);
        Assert.IsType<OkObjectResult>(await ruleController.Create(new("Checkers", "Groceries")));
        var batch = await Preview(db, user);
        Assert.Equal("Groceries", batch.Rows[0].Category); Assert.DoesNotContain("unavailable", batch.NoticesJson);
        Assert.IsType<OkObjectResult>(await Statements(db, user).Confirm(batch.Id, Decisions(batch)));
        var imported = await db.Transactions.SingleAsync(t => t.UserId == user);
        var controller = AsUser(new TransactionController(db, new Offline()), user);
        Assert.IsType<OkObjectResult>(controller.Create(new() { Description = "Bus", Date = new(2026, 9, 1), Amount = 50, Type = "expense", Category = "Transport" }));
        var ids = await db.Transactions.Where(t => t.UserId == user).Select(t => t.Id).ToListAsync();
        Assert.IsType<OkObjectResult>(await controller.BulkCategory(new(ids, "Shopping")));
        Assert.IsType<NotFoundObjectResult>(await AsUser(new TransactionController(db, new Offline()), other).Delete(new(ids)));
        Assert.IsType<OkObjectResult>(await controller.Delete(new(ids)));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Transactions.Where(t => t.UserId == user).ToListAsync());
        Assert.Equal(2, await db.Transactions.IgnoreQueryFilters().CountAsync(t => t.UserId == user && t.DeletedAt != null));
        Assert.Equal(0, (await new MonthlyAnalytics(db).Build(user, new(2026, 9, 1))).Spent);
        // Retrying confirmation after deletion must not resurrect transactions or break provenance.
        Assert.IsType<OkObjectResult>(await Statements(db, user).Confirm(batch.Id, Decisions(batch)));
        Assert.Empty(await db.Transactions.Where(t => t.UserId == user).ToListAsync());
        Assert.IsType<OkObjectResult>(await controller.Restore(new(ids)));
        db.ChangeTracker.Clear();
        Assert.All(await db.Transactions.Where(t => t.UserId == user).ToListAsync(), row => Assert.Equal("Shopping", row.Category));
        Assert.Equal(batch.Rows[0].Id, (await db.Transactions.SingleAsync(t => t.Id == imported.Id)).ImportRowId);
        Assert.Equal(1250, (await new MonthlyAnalytics(db).Build(user, new(2026, 9, 1))).Spent);
    }
}
