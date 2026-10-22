using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Api.Tests.Controllers;

public class TransactionWorkflowTests
{
    private sealed class Classifier(string? category) : IClassifierClient
    {
        public int Calls { get; private set; }
        public Task<string?> Suggest(string description, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(category); }
    }
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static T AsUser<T>(T controller, int id = 1) where T : ControllerBase
    {
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", id.ToString())], "Tests")) } };
        return controller;
    }
    private static Transaction Row(int user = 1, decimal amount = -100) => new() {
        UserId = user, Date = new DateTime(2026, 9, 10), Description = "Checkers groceries", Amount = amount, Category = "Groceries" };
    private static JsonElement Json(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task CashFlowUsesInclusiveCalendarDatesAndExcludesDeletedAndOtherUsers()
    {
        await using var db = Database();
        var before = Row(amount: 900); before.Date = new(2026, 9, 30);
        var first = Row(amount: 1000); first.Date = new(2026, 10, 1);
        var last = Row(amount: -125.50m); last.Date = new(2026, 10, 31);
        var after = Row(amount: -500); after.Date = new(2026, 11, 1);
        var deleted = Row(amount: -200); deleted.Date = first.Date; deleted.DeletedAt = DateTime.UtcNow;
        var other = Row(user: 2, amount: 5000); other.Date = last.Date;
        db.Transactions.AddRange(before, first, last, after, deleted, other);
        await db.SaveChangesAsync();
        var controller = AsUser(new TransactionController(db, new Classifier(null)));

        Assert.Equal(874.50m, Json(controller.GetBalance(new(2026, 10, 1), new(2026, 10, 31)))
            .GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(-125.50m, Json(controller.GetBalance(new(2026, 10, 31), new(2026, 10, 31)))
            .GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(1000m, Json(controller.GetBalance(new(2026, 10, 1, 23, 0, 0), new(2026, 10, 1)))
            .GetProperty("netCashFlow").GetDecimal());
    }

    [Fact]
    public async Task CashFlowSupportsOptionalBoundsEmptyPeriodsAndRejectsReversedDates()
    {
        await using var db = Database();
        var earlier = Row(amount: 1000); earlier.Date = new(2026, 9, 30);
        var later = Row(amount: -150); later.Date = new(2026, 10, 1);
        db.Transactions.AddRange(earlier, later); await db.SaveChangesAsync();
        var controller = AsUser(new TransactionController(db, new Classifier(null)));

        Assert.Equal(850m, Json(controller.GetBalance()).GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(-150m, Json(controller.GetBalance(from: new(2026, 10, 1))).GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(1000m, Json(controller.GetBalance(to: new(2026, 9, 30))).GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(0m, Json(controller.GetBalance(new(2026, 11, 1), new(2026, 11, 30))).GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(0m, Json(AsUser(new TransactionController(db, new Classifier(null)), 3).GetBalance())
            .GetProperty("netCashFlow").GetDecimal());
        Assert.IsType<BadRequestObjectResult>(controller.GetBalance(new(2026, 10, 2), new(2026, 10, 1)));
    }

    [Fact]
    public async Task DeleteAndRestorePreserveRecordsAndRecalculateEverySummary()
    {
        await using var db = Database();
        var expense = Row(); expense.ImportRowId = 25;
        var income = Row(amount: 500);
        db.Transactions.AddRange(expense, income);
        db.MonthlyBudgets.Add(new() { UserId = 1, Year = 2026, Month = 9, LimitAmount = 1000 });
        await db.SaveChangesAsync();
        var controller = AsUser(new TransactionController(db, new Classifier(null)));
        Assert.Equal(100, (await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1))).Spent);
        Assert.IsType<OkObjectResult>(await controller.Delete(new([expense.Id])));
        Assert.IsType<OkObjectResult>(await controller.Delete(new([expense.Id])));
        Assert.Single(await db.Transactions.ToListAsync());
        Assert.Equal(2, await db.Transactions.IgnoreQueryFilters().CountAsync());
        Assert.Single(Json(controller.Get(null, null)).EnumerateArray());
        Assert.Equal(expense.Id, Json(controller.Get(null, null, deleted: true))[0].GetProperty("Id").GetInt32());
        var summary = await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1));
        Assert.Equal(0, summary.Spent); Assert.Equal(1000, summary.Budget); Assert.Equal(500, summary.NetCashFlow);
        Assert.Equal(0, summary.Trends.Last().Expenses);
        Assert.Equal(500, Json(controller.GetBalance()).GetProperty("netCashFlow").GetDecimal());
        Assert.IsType<NotFoundObjectResult>(controller.Update(expense.Id, new() { Description = "Hidden", Amount = -5 }));
        Assert.IsType<OkObjectResult>(await controller.Restore(new([expense.Id])));
        Assert.IsType<OkObjectResult>(await controller.Restore(new([expense.Id])));
        Assert.Equal(25, (await db.Transactions.SingleAsync(t => t.Id == expense.Id)).ImportRowId);
        Assert.Equal(100, (await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1))).Spent);
    }

    [Fact]
    public async Task BulkMutationsRejectMixedOwnershipAndMissingRowsWithoutPartialChanges()
    {
        await using var db = Database();
        var first = Row(); var second = Row(); var other = Row(2);
        db.Transactions.AddRange(first, second, other); await db.SaveChangesAsync();
        var controller = AsUser(new TransactionController(db, new Classifier(null)));
        Assert.IsType<NotFoundObjectResult>(await controller.BulkCategory(new([first.Id, other.Id], "Transport")));
        Assert.IsType<NotFoundObjectResult>(await controller.Delete(new([first.Id, other.Id])));
        Assert.IsType<NotFoundObjectResult>(await controller.Restore(new([first.Id, other.Id])));
        Assert.IsType<NotFoundObjectResult>(await controller.BulkCategory(new([first.Id, 999], "Transport")));
        Assert.Equal("Groceries", first.Category); Assert.Null(first.DeletedAt);
        Assert.IsType<BadRequestObjectResult>(await controller.Delete(new([])));
        Assert.IsType<BadRequestObjectResult>(await controller.Restore(new([first.Id, first.Id])));
        Assert.IsType<BadRequestObjectResult>(await controller.BulkCategory(new([first.Id], new string('x', 101))));
        Assert.IsType<OkObjectResult>(await controller.BulkCategory(new([first.Id, second.Id], "transport")));
        Assert.Equal("Transport", first.Category); Assert.Equal("Transport", second.Category); Assert.Equal("Groceries", other.Category);
        await controller.Delete(new([first.Id]));
        Assert.IsType<NotFoundObjectResult>(await controller.BulkCategory(new([first.Id, second.Id], "Food")));
        Assert.Equal("Transport", second.Category);
    }

    [Theory]
    [InlineData("income", 100, 100)]
    [InlineData("expense", 100, -100)]
    [InlineData(null, -100, -100)]
    public async Task EntryTypeSignsPositiveAmountsAndEditingCanChangeDirection(string? type, decimal input, decimal expected)
    {
        await using var db = Database(); var controller = AsUser(new TransactionController(db, new Classifier(null)));
        Assert.IsType<OkObjectResult>(controller.Create(new() { Description = "Entry", Amount = input, Type = type }));
        var row = await db.Transactions.SingleAsync(); Assert.Equal(expected, row.Amount);
        Assert.IsType<OkObjectResult>(controller.Update(row.Id, new() { Description = "Income", Amount = 200, Type = "income" }));
        Assert.Equal(200, row.Amount);
        Assert.IsType<BadRequestObjectResult>(controller.Update(row.Id, new() { Description = "Bad sign", Amount = -10, Type = "expense" }));
        Assert.Equal(200, row.Amount); Assert.Equal("Income", row.Description);
    }

    [Fact]
    public async Task InvalidAmountsDoNotCreateTransactions()
    {
        await using var db = Database(); var controller = AsUser(new TransactionController(db, new Classifier(null)));
        foreach (var amount in new[] { 0m, -1m, 1.001m, decimal.MinValue, decimal.MaxValue })
            Assert.IsType<BadRequestObjectResult>(controller.Create(new() { Description = "Invalid", Amount = amount, Type = "expense" }));
        Assert.IsType<BadRequestObjectResult>(controller.Create(new() { Description = "Invalid", Amount = 10, Type = "outcome" }));
        Assert.Empty(await db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task RulesAreUserOwnedNormalizedEditableAndUniqueIgnoringCase()
    {
        await using var db = Database(); var controller = AsUser(new CategoryRulesController(db));
        var id = Json(await controller.Create(new("  CHECKERS   Store ", "groceries"))).GetProperty("Id").GetInt32();
        var row = await db.CategorizationRules.SingleAsync(); Assert.Equal("CHECKERS Store", row.Pattern); Assert.Equal("Groceries", row.Category);
        Assert.IsType<ConflictObjectResult>(await controller.Create(new("checkers store", "Shopping")));
        var other = AsUser(new CategoryRulesController(db), 2);
        Assert.Empty(Json(await other.Get()).EnumerateArray());
        Assert.IsType<NotFoundResult>(await other.Update(id, new("Checkers", "Shopping")));
        Assert.IsType<NotFoundResult>(await other.Delete(id));
        Assert.IsType<OkObjectResult>(await other.Create(new("checkers store", "Shopping")));
        Assert.IsType<OkObjectResult>(await controller.Update(id, new("Checkers", "Food", false)));
        Assert.False(row.Enabled); Assert.Equal("Food", row.Category);
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new("  ", "Food")));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new("Shop", new string('x', 101))));
        Assert.IsType<NoContentResult>(await controller.Delete(id));
        Assert.Single(await db.CategorizationRules.ToListAsync());
    }

    [Fact]
    public async Task MostSpecificEnabledRuleOverridesModelAndOtherUsersRulesAreIgnored()
    {
        await using var db = Database();
        db.CategorizationRules.AddRange(
            new() { UserId = 1, Pattern = "Checkers", Category = "Groceries" },
            new() { UserId = 1, Pattern = "Checkers delivery", Category = "Food" },
            new() { UserId = 1, Pattern = "Checkers delivery Cape Town", Category = "Shopping", Enabled = false },
            new() { UserId = 2, Pattern = "Checkers delivery Cape Town", Category = "Fees" });
        await db.SaveChangesAsync();
        var classifier = new Classifier("Transport"); var controller = AsUser(new TransactionController(db, classifier));
        var result = Json(await controller.Classify(new() { Description = "CHECKERS   delivery Cape Town" }));
        Assert.Equal("Food", result.GetProperty("Category").GetString()); Assert.Equal("rule", result.GetProperty("source").GetString()); Assert.Equal(0, classifier.Calls);
        result = Json(await controller.Classify(new() { Description = "Taxi" }));
        Assert.Equal("Transport", result.GetProperty("category").GetString()); Assert.Equal(1, classifier.Calls);
    }

    [Fact]
    public async Task ImportRulesKeepWorkingAfterClassifierOutageAndRemainEditable()
    {
        await using var db = Database();
        db.CategorizationRules.Add(new() { UserId = 1, Pattern = "Checkers", Category = "Groceries" }); await db.SaveChangesAsync();
        var classifier = new Classifier(null);
        var controller = AsUser(new StatementController(db, new StatementParser(), classifier, new MonthlyAnalytics(db)));
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n2026-09-01,Unknown,-10\n2026-09-02,CHECKERS,-100\n"));
        Assert.IsType<OkObjectResult>(await controller.Preview(new FormFile(source, 0, source.Length, "file", "test.csv")));
        var batch = await db.ImportBatches.Include(b => b.Rows).SingleAsync();
        Assert.Equal(1, classifier.Calls); Assert.Equal("Groceries", batch.Rows[1].Category); Assert.Empty(await db.Transactions.ToListAsync());
        var decisions = batch.Rows.Select(row => new StatementController.RowDecision(row.Id, true, "Food")).ToList();
        Assert.IsType<OkObjectResult>(await controller.Confirm(batch.Id, new(decisions)));
        Assert.All(await db.Transactions.ToListAsync(), row => Assert.Equal("Food", row.Category));
    }
}
