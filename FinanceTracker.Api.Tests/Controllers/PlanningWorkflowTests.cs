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

public class PlanningWorkflowTests
{
    private sealed class Offline : IClassifierClient { public Task<string?> Suggest(string description, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null); }
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static T AsUser<T>(T controller, int id = 1) where T : ControllerBase
    {
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", id.ToString())], "Tests")) } };
        return controller;
    }
    private static JsonElement Json(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
    private static RecurringBill Bill() => new() { UserId = 1, Name = "Streaming", MatchText = "Netflix", Amount = 100, Category = "Entertainment", Frequency = "monthly", FirstDueDate = new(2026, 9, 10) };
    private static Transaction Expense(decimal amount = -100, int user = 1) => new() { UserId = user, Description = "NETFLIX membership", Amount = amount, Date = new(2026, 9, 10), Category = "Entertainment" };

    [Theory]
    [InlineData("monthly", "2026-01-31", "2026-02-01", "2026-02-28")]
    [InlineData("monthly", "2026-01-31", "2026-03-01", "2026-03-31")]
    [InlineData("yearly", "2024-02-29", "2025-02-01", "2025-02-28")]
    [InlineData("yearly", "2024-02-29", "2028-02-01", "2028-02-29")]
    [InlineData("fortnightly", "2026-10-30", "2026-11-01", "2026-11-13,2026-11-27")]
    [InlineData("weekly", "2026-10-30", "2026-11-01", "2026-11-06,2026-11-13,2026-11-20,2026-11-27")]
    public void CalendarRecurrencesPreserveAnchorWithoutDrifting(string frequency, string anchor, string month, string expected)
    {
        var bill = Bill(); bill.Frequency = frequency; bill.FirstDueDate = DateTime.Parse(anchor);
        var start = DateTime.Parse(month);
        Assert.Equal(expected, string.Join(",", BillSchedule.Dates(bill, start, start.AddMonths(1).AddDays(-1)).Select(d => d.ToString("yyyy-MM-dd"))));
    }

    [Fact]
    public void RecurrenceHonorsInclusiveEndAndDoesNotGenerateBeforeStart()
    {
        var bill = Bill(); bill.EndDate = new(2026, 10, 10);
        Assert.Empty(BillSchedule.Dates(bill, new(2026, 8, 1), new(2026, 8, 31)));
        Assert.Single(BillSchedule.Dates(bill, new(2026, 10, 1), new(2026, 10, 31)));
        Assert.Empty(BillSchedule.Dates(bill, new(2026, 11, 1), new(2026, 11, 30)));
    }

    [Fact]
    public async Task SchedulesDoNotSpendMoneyAndPaymentsAreRetrySafeRecoverableAndOwned()
    {
        await using var db = Database(); var bill = Bill(); db.RecurringBills.Add(bill); await db.SaveChangesAsync();
        var controller = AsUser(new RecurringBillsController(db));
        Assert.Single(Json(await controller.Occurrences(2026, 9)).EnumerateArray()); Assert.Empty(await db.Transactions.ToListAsync());
        Assert.IsType<NotFoundResult>(await AsUser(new RecurringBillsController(db), 2).Pay(bill.Id, new(new(2026, 9, 10))));
        var request = new RecurringBillsController.PaymentRequest(new(2026, 9, 10), new(2026, 9, 11), 90);
        Assert.IsType<OkObjectResult>(await controller.Pay(bill.Id, request));
        Assert.IsType<OkObjectResult>(await controller.Pay(bill.Id, request));
        var transaction = await db.Transactions.SingleAsync(); Assert.Equal(-90, transaction.Amount);
        Assert.Equal("paid", Json(await controller.Occurrences(2026, 9))[0].GetProperty("Status").GetString());
        var transactions = AsUser(new TransactionController(db, new Offline()));
        await transactions.Delete(new([transaction.Id]));
        Assert.Equal("payment-deleted", Json(await controller.Occurrences(2026, 9))[0].GetProperty("Status").GetString());
        Assert.IsType<ConflictObjectResult>(await controller.Pay(bill.Id, request)); Assert.Single(await db.Transactions.IgnoreQueryFilters().ToListAsync());
        await transactions.Restore(new([transaction.Id]));
        Assert.Equal("paid", Json(await controller.Occurrences(2026, 9))[0].GetProperty("Status").GetString());
        Assert.IsType<NoContentResult>(await controller.Archive(bill.Id));
        Assert.Empty(Json(await controller.Get()).EnumerateArray());
        Assert.Equal("paid", Json(await controller.Occurrences(2026, 9))[0].GetProperty("Status").GetString());
        Assert.Empty(Json(await controller.Occurrences(2026, 10)).EnumerateArray());
    }

    [Fact]
    public async Task ChangedOrDeletedPaymentsCanBeCorrectedOrUnlinkedWithoutDeletingLedgerHistory()
    {
        await using var db = Database(); var bill = Bill(); db.RecurringBills.Add(bill); await db.SaveChangesAsync();
        var controller = AsUser(new RecurringBillsController(db)); var request = new RecurringBillsController.PaymentRequest(new(2026, 9, 10));
        Assert.IsType<OkObjectResult>(await controller.Pay(bill.Id, request));
        var transaction = await db.Transactions.SingleAsync(); var transactions = AsUser(new TransactionController(db, new Offline()));
        Assert.IsType<OkObjectResult>(transactions.Update(transaction.Id, new() { Description = transaction.Description, Category = transaction.Category, Amount = 100, Type = "income" }));
        Assert.Equal("payment-changed", Json(await controller.Occurrences(2026, 9))[0].GetProperty("Status").GetString());
        Assert.IsType<ConflictObjectResult>(await controller.Pay(bill.Id, request));
        Assert.Empty(await RecurringPaymentMatcher.Load(db, 1));
        Assert.IsType<OkObjectResult>(transactions.Update(transaction.Id, new() { Description = transaction.Description, Category = transaction.Category, Amount = 100, Type = "expense" }));
        Assert.IsType<OkObjectResult>(await transactions.Delete(new([transaction.Id])));
        Assert.IsType<NoContentResult>(await controller.Unlink(bill.Id, new(2026, 9, 10)));
        Assert.Empty(await db.BillPayments.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.Transactions.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ExistingExpensesMustBeLinkedOrExplicitlyRecordedAsSeparate()
    {
        await using var db = Database(); var bill = Bill(); var expense = Expense(); var other = Expense(user: 2);
        db.RecurringBills.Add(bill); db.Transactions.AddRange(expense, other); await db.SaveChangesAsync();
        var controller = AsUser(new RecurringBillsController(db));
        Assert.Single(Json(await controller.Candidates(bill.Id, new(2026, 9, 10))).EnumerateArray());
        Assert.IsType<ConflictObjectResult>(await controller.Pay(bill.Id, new(new(2026, 9, 10))));
        Assert.IsType<NotFoundObjectResult>(await controller.Pay(bill.Id, new(new(2026, 9, 10), TransactionId: other.Id)));
        Assert.IsType<OkObjectResult>(await controller.Pay(bill.Id, new(new(2026, 9, 10), TransactionId: expense.Id)));
        Assert.Equal(2, await db.Transactions.CountAsync());
        var another = Bill(); db.RecurringBills.Add(another); await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await controller.Pay(another.Id, new(new(2026, 9, 10), TransactionId: expense.Id)));
        Assert.IsType<NoContentResult>(await controller.Unlink(bill.Id, new(2026, 9, 10)));
        Assert.Equal(2, await db.Transactions.CountAsync());
        Assert.IsType<OkObjectResult>(await controller.Pay(bill.Id, new(new(2026, 9, 10), ConfirmSeparateExpense: true)));
        Assert.Equal(3, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task PausingEditingAndValidationDoNotRemoveRecordedHistory()
    {
        await using var db = Database(); var controller = AsUser(new RecurringBillsController(db));
        var request = new RecurringBillsController.BillRequest("Rent", "RENT", 5000, "Rent", "monthly", new(2026, 9, 1));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(request with { Amount = -1 }));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(request with { EndDate = new(2026, 8, 1) }));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(request with { Frequency = "daily" }));
        Assert.IsType<OkObjectResult>(await controller.Create(request)); var bill = await db.RecurringBills.SingleAsync();
        Assert.IsType<OkObjectResult>(await controller.Pay(bill.Id, new(new(2026, 9, 1))));
        Assert.IsType<OkObjectResult>(await controller.Update(bill.Id, request with { FirstDueDate = new(2026, 10, 1), Enabled = false }));
        Assert.Single(Json(await controller.Occurrences(2026, 9)).EnumerateArray());
        Assert.Empty(Json(await controller.Occurrences(2026, 10)).EnumerateArray());
        Assert.IsType<ConflictObjectResult>(await controller.Pay(bill.Id, new(new(2026, 10, 1))));
        Assert.IsType<NotFoundResult>(await AsUser(new RecurringBillsController(db), 2).Update(bill.Id, request));
        Assert.IsType<NotFoundResult>(await AsUser(new RecurringBillsController(db), 2).Archive(bill.Id));
        Assert.Empty(Json(await AsUser(new RecurringBillsController(db), 2).Occurrences(2026, 9)).EnumerateArray());
    }

    [Fact]
    public async Task ImportPreviewAndConfirmationDetectRecordedRecurringExpenses()
    {
        await using var db = Database(); var bill = Bill(); db.RecurringBills.Add(bill); await db.SaveChangesAsync();
        var statements = AsUser(new StatementController(db, new StatementParser(), new Offline(), new MonthlyAnalytics(db)));
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n2026-09-12,NETFLIX card 442,-100\n"));
        await statements.Preview(new FormFile(source, 0, source.Length, "file", "test.csv"));
        var preview = await db.ImportBatches.Include(b => b.Rows).SingleAsync(); Assert.False(preview.Rows[0].PossibleDuplicate);
        await AsUser(new RecurringBillsController(db)).Pay(bill.Id, new(new(2026, 9, 10)));
        Assert.IsType<ConflictObjectResult>(await statements.Confirm(preview.Id, new([new(preview.Rows[0].Id, true, "Entertainment")])));
        source.Position = 0;
        await statements.Preview(new FormFile(source, 0, source.Length, "file", "test.csv"));
        var repeated = await db.ImportBatches.Include(b => b.Rows).SingleAsync(b => b.Id != preview.Id);
        Assert.True(repeated.Rows[0].PossibleDuplicate); Assert.False(repeated.Rows[0].Included);
        Assert.IsType<OkObjectResult>(await statements.Confirm(repeated.Id, new([new(repeated.Rows[0].Id, true, "Entertainment", true)])));
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    [Theory]
    [InlineData(799, "none")]
    [InlineData(800, "approaching")]
    [InlineData(1000, "reached")]
    [InlineData(1001, "over")]
    public async Task BudgetWarningsUseExactThresholdsAndIgnoreIncomeAndDeletedExpenses(decimal spent, string level)
    {
        await using var db = Database(); db.MonthlyBudgets.Add(new() { UserId = 1, Year = 2026, Month = 9, LimitAmount = 1000,
            CategoryLimits = [new() { Category = "Entertainment", LimitAmount = 1000 }] });
        db.Transactions.AddRange(Expense(-spent), Expense(5000), new Transaction { UserId = 1, Date = new(2026, 9, 10), Description = "Deleted", Amount = -9999, DeletedAt = DateTime.UtcNow }, Expense(-9999, 2));
        await db.SaveChangesAsync(); var summary = await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1));
        if (level == "none") Assert.Empty(summary.Warnings);
        else { Assert.Equal(2, summary.Warnings.Count); Assert.All(summary.Warnings, warning => Assert.Equal(level, warning.Level)); }
    }

    [Fact]
    public async Task WarningSettingsArePersistentUserSpecificAndHandleZeroAndUnconfirmedBudgets()
    {
        await using var db = Database(); var analytics = new MonthlyAnalytics(db); var controller = AsUser(new BudgetController(db, analytics));
        db.MonthlyBudgets.Add(new() { UserId = 1, Year = 2026, Month = 9, LimitAmount = 0 });
        await db.SaveChangesAsync(); Assert.Empty((await analytics.Build(1, new(2026, 9, 1))).Warnings);
        db.Transactions.Add(Expense()); await db.SaveChangesAsync();
        var summary = await analytics.Build(1, new(2026, 9, 1)); Assert.Equal("over", Assert.Single(summary.Warnings).Level); Assert.Null(summary.Warnings[0].PercentUsed);
        Assert.IsType<BadRequestObjectResult>(await controller.SetWarningSettings(new(true, 100)));
        Assert.IsType<OkObjectResult>(await controller.SetWarningSettings(new(false, 90)));
        Assert.Empty((await analytics.Build(1, new(2026, 9, 1))).Warnings);
        Assert.Equal(80, (await analytics.Build(2, new(2026, 9, 1))).WarningSettings.ThresholdPercent);
        await controller.SetWarningSettings(new(true, 90));
        var budget = await db.MonthlyBudgets.SingleAsync(); budget.LimitAmount = 120; await db.SaveChangesAsync();
        Assert.Empty((await analytics.Build(1, new(2026, 9, 1))).Warnings);
        await controller.SetWarningSettings(new(true, 80)); Assert.Single((await analytics.Build(1, new(2026, 9, 1))).Warnings);
        budget.NeedsReconfirmation = true; await db.SaveChangesAsync(); Assert.Empty((await analytics.Build(1, new(2026, 9, 1))).Warnings);
        Assert.Empty((await analytics.Build(1, new(2026, 10, 1))).Warnings);
    }
}
