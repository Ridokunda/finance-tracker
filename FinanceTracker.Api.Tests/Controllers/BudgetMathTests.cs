using System.Security.Claims;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Api.Tests.Controllers;

public class BudgetMathTests
{
    [Fact]
    public async Task GetBudgetSummary_ReturnsExpectedBudgetSpentAndRemaining()
    {
        await using var db = CreateDbContext();

        var now = DateTime.UtcNow;

        db.MonthlyBudgets.Add(new MonthlyBudget
        {
            UserId = 1,
            Year = now.Year,
            Month = now.Month,
            LimitAmount = 1000m
        });

        db.Transactions.AddRange(
            new Transaction { UserId = 1, Date = now.AddDays(-1), Description = "Groceries", Amount = -200m, Category = "Food" },
            new Transaction { UserId = 1, Date = now.AddDays(-2), Description = "Bus", Amount = -50m, Category = "Transport" },
            new Transaction { UserId = 1, Date = now.AddDays(-3), Description = "Salary", Amount = 300m, Category = "Income" },
            new Transaction { UserId = 2, Date = now.AddDays(-1), Description = "Other User", Amount = -500m, Category = "Food" }
        );

        await db.SaveChangesAsync();

        var controller = new BudgetController(db);
        AttachUser(controller, userId: 1);

        var result = await controller.GetBudgetSummary();

        var ok = Assert.IsType<OkObjectResult>(result);
        var summary = Assert.IsType<BudgetController.BudgetSummaryResponse>(ok.Value);

        Assert.Equal(1000m, summary.Budget);
        Assert.Equal(250m, summary.Spent);
        Assert.Equal(750m, summary.Remaining);
        Assert.Equal("ok", summary.Status);
    }

    [Fact]
    public async Task GetMonthlyBudget_ReturnsRemainingAsLimitMinusExpenses()
    {
        await using var db = CreateDbContext();

        const int year = 2026;
        const int month = 7;

        db.MonthlyBudgets.Add(new MonthlyBudget
        {
            UserId = 1,
            Year = year,
            Month = month,
            LimitAmount = 1200m
        });

        db.Transactions.AddRange(
            new Transaction { UserId = 1, Date = new DateTime(year, month, 5, 0, 0, 0, DateTimeKind.Utc), Description = "Rent", Amount = -800m, Category = "Rent" },
            new Transaction { UserId = 1, Date = new DateTime(year, month, 10, 0, 0, 0, DateTimeKind.Utc), Description = "Food", Amount = -150m, Category = "Food" },
            new Transaction { UserId = 1, Date = new DateTime(year, month, 12, 0, 0, 0, DateTimeKind.Utc), Description = "Refund", Amount = 100m, Category = "Income" },
            new Transaction { UserId = 2, Date = new DateTime(year, month, 9, 0, 0, 0, DateTimeKind.Utc), Description = "Other User", Amount = -999m, Category = "Food" }
        );

        await db.SaveChangesAsync();

        var controller = new StatementController(db);
        AttachUser(controller, userId: 1);

        var result = await controller.GetMonthlyBudget(year, month);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);

        var totalExpenses = doc.RootElement.GetProperty("TotalExpenses").GetDecimal();
        var remaining = doc.RootElement.GetProperty("Remaining").GetDecimal();

        Assert.Equal(950m, totalExpenses);
        Assert.Equal(250m, remaining);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void AttachUser(ControllerBase controller, int userId)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("UserId", userId.ToString())],
            "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }
}
