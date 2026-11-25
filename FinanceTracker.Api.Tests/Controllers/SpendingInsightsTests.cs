using System.Security.Claims;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Api.Tests.Controllers;

public class SpendingInsightsTests
{
    private static ApplicationDbContext Database() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static T AsUser<T>(T controller, int id = 1) where T : ControllerBase
    {
        controller.ControllerContext = new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", id.ToString())], "Tests"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task SavingsRateAndTrendMetrics_HandlePositiveDeficitAndZeroIncome()
    {
        await using var db = Database();
        db.Transactions.AddRange(
            // July: zero income -> null savings rate
            new Transaction { UserId = 1, Date = new(2026, 7, 5), Description = "Bus", Amount = -200m, Category = "Transport" },
            // August: deficit spending (income 1000, spent 1250 -> -25.0%)
            new Transaction { UserId = 1, Date = new(2026, 8, 1), Description = "Part-time", Amount = 1000m, Category = "Income" },
            new Transaction { UserId = 1, Date = new(2026, 8, 10), Description = "Rent", Amount = -1250m, Category = "Rent" },
            // September: positive savings (income 20000, spent 12500 -> 37.5%)
            new Transaction { UserId = 1, Date = new(2026, 9, 1), Description = "Salary", Amount = 20000m, Category = "Income" },
            new Transaction { UserId = 1, Date = new(2026, 9, 2), Description = "Rent", Amount = -10000m, Category = "Rent" },
            new Transaction { UserId = 1, Date = new(2026, 9, 5), Description = "Groceries", Amount = -2500m, Category = "Groceries" },
            // Soft-deleted & other user rows must be excluded
            new Transaction { UserId = 1, Date = new(2026, 9, 8), Description = "Deleted", Amount = -9000m, Category = "Shopping", DeletedAt = DateTime.UtcNow },
            new Transaction { UserId = 2, Date = new(2026, 9, 8), Description = "Other user", Amount = -9000m, Category = "Shopping" }
        );
        await db.SaveChangesAsync();

        var summary = await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1));

        Assert.Equal(37.5m, summary.SavingsRate);
        Assert.Equal(-25.0m, summary.PreviousMonthSavingsRate);
        Assert.Equal(6, summary.Trends.Count);

        var july = summary.Trends.Single(t => t.Month == "2026-07");
        Assert.Equal(-200m, july.NetCashFlow);
        Assert.Null(july.SavingsRate);

        var august = summary.Trends.Single(t => t.Month == "2026-08");
        Assert.Equal(-250m, august.NetCashFlow);
        Assert.Equal(-25.0m, august.SavingsRate);

        var september = summary.Trends.Single(t => t.Month == "2026-09");
        Assert.Equal(7500m, september.NetCashFlow);
        Assert.Equal(37.5m, september.SavingsRate);
    }

    [Fact]
    public async Task PeriodComparisons_ComputeMonthOverMonthAndYearOverYearDeltas()
    {
        await using var db = Database();
        db.Transactions.AddRange(
            // Year ago (2025-09): Groceries 800, Transport 200 -> Total 1000
            new Transaction { UserId = 1, Date = new(2025, 9, 5), Description = "Checkers", Amount = -800m, Category = "Groceries" },
            new Transaction { UserId = 1, Date = new(2025, 9, 6), Description = "MyCiTi", Amount = -200m, Category = "Transport" },
            // Previous month (2026-08): Groceries 1000 -> Total 1000
            new Transaction { UserId = 1, Date = new(2026, 8, 5), Description = "Checkers", Amount = -1000m, Category = "Groceries" },
            // Current month (2026-09): Groceries 1200 (+20% MoM, +50% YoY), Transport 150 (new MoM, -25% YoY) -> Total 1350
            new Transaction { UserId = 1, Date = new(2026, 9, 5), Description = "Checkers", Amount = -1200m, Category = "Groceries" },
            new Transaction { UserId = 1, Date = new(2026, 9, 7), Description = "MyCiTi", Amount = -150m, Category = "Transport" }
        );
        await db.SaveChangesAsync();

        var summary = await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1));

        Assert.NotNull(summary.Comparisons);
        Assert.Equal(1000m, summary.Comparisons!.MonthOverMonth.Previous);
        Assert.Equal(350m, summary.Comparisons.MonthOverMonth.Change);
        Assert.Equal(35.0m, summary.Comparisons.MonthOverMonth.ChangePercent);
        Assert.Equal(1000m, summary.Comparisons.YearOverYear.Previous);
        Assert.Equal(350m, summary.Comparisons.YearOverYear.Change);
        Assert.Equal(35.0m, summary.Comparisons.YearOverYear.ChangePercent);

        var groceries = summary.Categories.Single(c => c.Category == "Groceries");
        Assert.Equal(1000m, groceries.PreviousMonthSpent);
        Assert.Equal(200m, groceries.MonthOverMonthChange);
        Assert.Equal(20.0m, groceries.MonthOverMonthPercent);
        Assert.Equal(800m, groceries.PreviousYearSpent);
        Assert.Equal(400m, groceries.YearOverYearChange);
        Assert.Equal(50.0m, groceries.YearOverYearPercent);

        var transport = summary.Categories.Single(c => c.Category == "Transport");
        Assert.Equal(0m, transport.PreviousMonthSpent);
        Assert.Equal(150m, transport.MonthOverMonthChange);
        Assert.Null(transport.MonthOverMonthPercent);
        Assert.Equal(200m, transport.PreviousYearSpent);
        Assert.Equal(-50m, transport.YearOverYearChange);
        Assert.Equal(-25.0m, transport.YearOverYearPercent);
    }

    [Fact]
    public async Task TopMerchants_NormalizeBankNoiseGroupCaseInsensitivelyAndTrackPreviousMonth()
    {
        await using var db = Database();
        db.Transactions.AddRange(
            // Previous month Checkers = 400
            new Transaction { UserId = 1, Date = new(2026, 8, 12), Description = "POS PURCHASE CHECKERS 0101", Amount = -400m, Category = "Groceries" },
            // Current month Checkers = 600 across 2 variants
            new Transaction { UserId = 1, Date = new(2026, 9, 2), Description = "POS PURCHASE Checkers 0412", Amount = -350m, Category = "Groceries" },
            new Transaction { UserId = 1, Date = new(2026, 9, 10), Description = "Checkers #9981", Amount = -250m, Category = "Groceries" },
            // Current month Woolworths = 400
            new Transaction { UserId = 1, Date = new(2026, 9, 14), Description = "Woolworths Food *88321", Amount = -400m, Category = "Food" },
            // Income, deleted expense, and other user must not appear
            new Transaction { UserId = 1, Date = new(2026, 9, 1), Description = "Salary", Amount = 25000m, Category = "Income" },
            new Transaction { UserId = 1, Date = new(2026, 9, 15), Description = "Deleted Shop", Amount = -5000m, Category = "Shopping", DeletedAt = DateTime.UtcNow },
            new Transaction { UserId = 2, Date = new(2026, 9, 15), Description = "Other Shop", Amount = -5000m, Category = "Shopping" }
        );
        await db.SaveChangesAsync();

        var summary = await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1));
        Assert.NotNull(summary.TopMerchants);
        Assert.Equal(2, summary.TopMerchants!.Count);

        var checkers = summary.TopMerchants[0];
        Assert.Equal("Checkers", checkers.Merchant);
        Assert.Equal("Checkers", checkers.SearchQuery);
        Assert.Equal("Groceries", checkers.PrimaryCategory);
        Assert.Equal(600m, checkers.Spent);
        Assert.Equal(2, checkers.TransactionCount);
        Assert.Equal(60.0m, checkers.SharePercent);
        Assert.Equal(400m, checkers.PreviousMonthSpent);
        Assert.Equal(200m, checkers.MonthOverMonthChange);

        var woolworths = summary.TopMerchants[1];
        Assert.Equal("Woolworths Food", woolworths.Merchant);
        Assert.Equal(400m, woolworths.Spent);
        Assert.Equal(40.0m, woolworths.SharePercent);
        Assert.Equal(0m, woolworths.PreviousMonthSpent);
        Assert.Equal(400m, woolworths.MonthOverMonthChange);
    }

    [Fact]
    public async Task CategoryRollover_AccumulatesUnusedFundsAcrossConsecutiveMonthsAndClampsOverspend()
    {
        await using var db = Database();
        var analytics = new MonthlyAnalytics(db);
        var budgets = AsUser(new BudgetController(db, analytics));

        // July: Maintenance 500 (rollover ON), Groceries 1000 (rollover OFF). Spent: Maintenance 0, Groceries 600.
        Assert.IsType<OkObjectResult>(await budgets.SetBudget(new()
        {
            Year = 2026, Month = 7, LimitAmount = 2000,
            CategoryLimits = [new("Maintenance", 500, true), new("Groceries", 1000, false)]
        }));
        db.Transactions.Add(new Transaction { UserId = 1, Date = new(2026, 7, 10), Description = "Shop", Amount = -600m, Category = "Groceries" });

        // August: Copy July -> Maintenance 500 (+500 carry = 1000), spent 200 -> 800 remaining; Groceries 1000 (rollover OFF), spent 1200 (overspent)
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await budgets.CopyPrevious(new(2026, 8)));
        db.Transactions.AddRange(
            new Transaction { UserId = 1, Date = new(2026, 8, 12), Description = "Service", Amount = -200m, Category = "Maintenance" },
            new Transaction { UserId = 1, Date = new(2026, 8, 15), Description = "Big shop", Amount = -1200m, Category = "Groceries" }
        );
        await db.SaveChangesAsync();

        // September: Maintenance rollover ON (should get 800 carry -> 1300 effective), Groceries rollover ON (Aug overspent -> clamped to 0 carry -> 1000 effective)
        Assert.IsType<OkObjectResult>(await budgets.SetBudget(new()
        {
            Year = 2026, Month = 9, LimitAmount = 2000,
            CategoryLimits = [new("Maintenance", 500, true), new("Groceries", 1000, true)]
        }));
        db.Transactions.Add(new Transaction { UserId = 1, Date = new(2026, 9, 10), Description = "Tyres", Amount = -900m, Category = "Maintenance" });
        await db.SaveChangesAsync();

        // Updating overall limit without passing CategoryLimits must preserve RolloverEnabled.
        Assert.IsType<OkObjectResult>(await budgets.SetBudget(new() { Year = 2026, Month = 9, LimitAmount = 2500 }));

        var sep = await analytics.Build(1, new(2026, 9, 1));
        var maintenance = sep.Categories.Single(c => c.Category == "Maintenance");
        Assert.True(maintenance.RolloverEnabled);
        Assert.Equal(500m, maintenance.Limit);
        Assert.Equal(800m, maintenance.RolloverAmount);
        Assert.Equal(1300m, maintenance.EffectiveLimit);
        Assert.Equal(400m, maintenance.Remaining);
        Assert.Equal("Within budget", maintenance.Status);
        // 900 / 1300 = 69.2% < 80% warning threshold -> no Maintenance warning despite 900 > 500 base limit.
        Assert.DoesNotContain(sep.Warnings, w => w.Category == "Maintenance");

        var groceries = sep.Categories.Single(c => c.Category == "Groceries");
        Assert.True(groceries.RolloverEnabled);
        Assert.Equal(0m, groceries.RolloverAmount);
        Assert.Equal(1000m, groceries.EffectiveLimit);

        // Turning rollover off in September removes the carry immediately.
        Assert.IsType<OkObjectResult>(await budgets.SetBudget(new()
        {
            Year = 2026, Month = 9, LimitAmount = 2500,
            CategoryLimits = [new("Maintenance", 500, false), new("Groceries", 1000, false)]
        }));
        var sepNoRollover = await analytics.Build(1, new(2026, 9, 1));
        var maintenanceOff = sepNoRollover.Categories.Single(c => c.Category == "Maintenance");
        Assert.False(maintenanceOff.RolloverEnabled);
        Assert.Equal(0m, maintenanceOff.RolloverAmount);
        Assert.Equal(500m, maintenanceOff.EffectiveLimit);
        Assert.Equal("Over budget", maintenanceOff.Status);
        Assert.Contains(sepNoRollover.Warnings, w => w.Category == "Maintenance" && w.Level == "over");
    }

    [Fact]
    public async Task CategoryRollover_IgnoresUnreconfirmedPriorBudgets()
    {
        await using var db = Database();
        db.MonthlyBudgets.AddRange(
            new MonthlyBudget
            {
                UserId = 1, Year = 2026, Month = 8, LimitAmount = 1000, NeedsReconfirmation = true,
                CategoryLimits = [new() { Category = "Utilities", LimitAmount = 500, RolloverEnabled = true }]
            },
            new MonthlyBudget
            {
                UserId = 1, Year = 2026, Month = 9, LimitAmount = 1000, NeedsReconfirmation = false,
                CategoryLimits = [new() { Category = "Utilities", LimitAmount = 500, RolloverEnabled = true }]
            }
        );
        await db.SaveChangesAsync();

        var summary = await new MonthlyAnalytics(db).Build(1, new(2026, 9, 1));
        var utilities = summary.Categories.Single(c => c.Category == "Utilities");
        Assert.Equal(0m, utilities.RolloverAmount);
        Assert.Equal(500m, utilities.EffectiveLimit);
    }
}
