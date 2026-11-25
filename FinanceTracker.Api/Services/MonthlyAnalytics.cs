using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Services;

public record PeriodDelta(decimal Previous, decimal Change, decimal? ChangePercent);
public record SpendingComparisons(PeriodDelta MonthOverMonth, PeriodDelta YearOverYear);
public record MerchantSummary(
    string Merchant,
    string SearchQuery,
    string PrimaryCategory,
    decimal Spent,
    int TransactionCount,
    decimal SharePercent,
    decimal PreviousMonthSpent,
    decimal? MonthOverMonthChange);
public record CategorySummary(
    string Category,
    decimal? Limit,
    decimal Spent,
    decimal? Remaining,
    decimal? Progress,
    string Status,
    bool RolloverEnabled = false,
    decimal RolloverAmount = 0m,
    decimal? EffectiveLimit = null,
    decimal PreviousMonthSpent = 0m,
    decimal? MonthOverMonthChange = null,
    decimal? MonthOverMonthPercent = null,
    decimal PreviousYearSpent = 0m,
    decimal? YearOverYearChange = null,
    decimal? YearOverYearPercent = null);
public record TrendSummary(string Month, decimal Income, decimal Expenses, decimal NetCashFlow = 0m, decimal? SavingsRate = null);
public record WarningPreferences(bool Enabled, int ThresholdPercent);
public record BudgetWarning(string Scope, string? Category, decimal Limit, decimal Spent, decimal? PercentUsed, string Level);
public record MonthlySummary(
    int Year,
    int Month,
    bool HasBudget,
    decimal Budget,
    decimal Spent,
    decimal Remaining,
    string Status,
    bool NeedsReconfirmation,
    decimal Income,
    decimal NetCashFlow,
    IReadOnlyList<CategorySummary> Categories,
    IReadOnlyList<TrendSummary> Trends,
    WarningPreferences WarningSettings,
    IReadOnlyList<BudgetWarning> Warnings,
    decimal? SavingsRate = null,
    decimal? PreviousMonthSavingsRate = null,
    SpendingComparisons? Comparisons = null,
    IReadOnlyList<MerchantSummary>? TopMerchants = null);

public class MonthlyAnalytics(ApplicationDbContext db)
{
    public async Task<MonthlySummary> Build(int userId, DateTime start)
    {
        var end = start.AddMonths(1);
        var history = start.AddMonths(-5);
        var previousMonthStart = start.AddMonths(-1);
        var previousYearStart = start.AddYears(-1);
        var previousYearEnd = previousYearStart.AddMonths(1);

        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId
                && ((t.Date >= history && t.Date < end) || (t.Date >= previousYearStart && t.Date < previousYearEnd)))
            .ToListAsync();

        var current = transactions.Where(t => t.Date >= start && t.Date < end).ToList();
        var previousMonth = transactions.Where(t => t.Date >= previousMonthStart && t.Date < start).ToList();
        var previousYear = transactions.Where(t => t.Date >= previousYearStart && t.Date < previousYearEnd).ToList();

        var budget = await db.MonthlyBudgets.AsNoTracking().Include(b => b.CategoryLimits)
            .SingleOrDefaultAsync(b => b.UserId == userId && b.Year == start.Year && b.Month == start.Month);

        var spent = -current.Where(t => t.Amount < 0).Sum(t => t.Amount);
        var income = current.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var previousMonthSpent = -previousMonth.Where(t => t.Amount < 0).Sum(t => t.Amount);
        var previousMonthIncome = previousMonth.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var previousYearSpent = -previousYear.Where(t => t.Amount < 0).Sum(t => t.Amount);

        var limits = budget?.CategoryLimits ?? [];
        var rollovers = await ComputeRollovers(userId, start, history, budget, transactions);

        var names = current.Select(t => FinanceRules.Category(t.Category)).Concat(limits.Select(c => c.Category))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();

        var categories = names.Select(name =>
        {
            var actual = SumCategoryExpenses(current, name);
            var prevMo = SumCategoryExpenses(previousMonth, name);
            var prevYr = SumCategoryExpenses(previousYear, name);
            var categoryLimit = limits.FirstOrDefault(c => c.Category.Equals(name, StringComparison.OrdinalIgnoreCase));
            var baseLimit = categoryLimit?.LimitAmount;
            var rolloverEnabled = categoryLimit?.RolloverEnabled ?? false;
            var rolloverAmount = rolloverEnabled && rollovers.TryGetValue(name, out var carried) ? carried : 0m;
            decimal? effectiveLimit = baseLimit == null ? null : baseLimit.Value + rolloverAmount;
            var remaining = effectiveLimit == null ? (decimal?)null : effectiveLimit.Value - actual;
            var progress = effectiveLimit > 0 ? actual / effectiveLimit.Value * 100m : (decimal?)null;
            var status = effectiveLimit == null ? "No limit set" : actual > effectiveLimit.Value ? "Over budget" : "Within budget";
            var momDelta = actual == 0m && prevMo == 0m ? null : Delta(actual, prevMo);
            var yoyDelta = actual == 0m && prevYr == 0m ? null : Delta(actual, prevYr);

            return new CategorySummary(
                name,
                baseLimit,
                actual,
                remaining,
                progress,
                status,
                rolloverEnabled,
                rolloverAmount,
                effectiveLimit,
                prevMo,
                momDelta?.Change,
                momDelta?.ChangePercent,
                prevYr,
                yoyDelta?.Change,
                yoyDelta?.ChangePercent);
        }).ToList();

        var trends = Enumerable.Range(0, 6).Select(i =>
        {
            var month = history.AddMonths(i);
            var rows = transactions.Where(t => t.Date >= month && t.Date < month.AddMonths(1)).ToList();
            var monthIncome = rows.Where(t => t.Amount > 0).Sum(t => t.Amount);
            var monthExpenses = -rows.Where(t => t.Amount < 0).Sum(t => t.Amount);
            return new TrendSummary(
                month.ToString("yyyy-MM"),
                monthIncome,
                monthExpenses,
                monthIncome - monthExpenses,
                SavingsRateOf(monthIncome, monthExpenses));
        }).ToList();

        var topMerchants = BuildTopMerchants(current, previousMonth, spent);
        var comparisons = new SpendingComparisons(Delta(spent, previousMonthSpent), Delta(spent, previousYearSpent));

        var amount = budget?.LimitAmount ?? 0;
        var savedSettings = await db.BudgetWarningSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId);
        var settings = new WarningPreferences(savedSettings?.Enabled ?? true, savedSettings?.ThresholdPercent ?? 80);
        var warnings = new List<BudgetWarning>();
        if (settings.Enabled && budget != null && !budget.NeedsReconfirmation && amount >= 0)
        {
            AddWarning(warnings, "overall", null, amount, spent, settings.ThresholdPercent);
            foreach (var category in categories.Where(c => c.EffectiveLimit != null))
                AddWarning(warnings, "category", category.Category, category.EffectiveLimit!.Value, category.Spent, settings.ThresholdPercent);
        }

        return new(
            start.Year,
            start.Month,
            budget != null,
            amount,
            spent,
            amount - spent,
            budget == null ? "unset" : spent > amount ? "over" : "ok",
            budget?.NeedsReconfirmation ?? false,
            income,
            income - spent,
            categories,
            trends,
            settings,
            warnings,
            SavingsRateOf(income, spent),
            SavingsRateOf(previousMonthIncome, previousMonthSpent),
            comparisons,
            topMerchants);
    }

    private async Task<Dictionary<string, decimal>> ComputeRollovers(
        int userId,
        DateTime start,
        DateTime loadedHistoryStart,
        MonthlyBudget? currentBudget,
        List<Transaction> loadedTransactions)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (currentBudget == null || currentBudget.NeedsReconfirmation) return result;
        var rolloverLimits = currentBudget.CategoryLimits.Where(c => c.RolloverEnabled).ToList();
        if (rolloverLimits.Count == 0) return result;

        var priorBudgets = await db.MonthlyBudgets.AsNoTracking().Include(b => b.CategoryLimits)
            .Where(b => b.UserId == userId && !b.NeedsReconfirmation
                && (b.Year < start.Year || (b.Year == start.Year && b.Month < start.Month)))
            .ToListAsync();
        if (priorBudgets.Count == 0) return result;

        var budgetsByMonth = priorBudgets.ToDictionary(b => new DateTime(b.Year, b.Month, 1));
        var chains = new Dictionary<string, List<(DateTime Month, decimal Limit)>>(StringComparer.OrdinalIgnoreCase);
        DateTime? earliestMonth = null;

        foreach (var limit in rolloverLimits)
        {
            var chain = new List<(DateTime Month, decimal Limit)>();
            var cursor = start.AddMonths(-1);
            while (budgetsByMonth.TryGetValue(cursor, out var priorBudget))
            {
                var priorLimit = priorBudget.CategoryLimits.FirstOrDefault(c => c.Category.Equals(limit.Category, StringComparison.OrdinalIgnoreCase));
                if (priorLimit == null) break;
                chain.Add((cursor, priorLimit.LimitAmount));
                if (earliestMonth == null || cursor < earliestMonth.Value) earliestMonth = cursor;
                if (!priorLimit.RolloverEnabled) break;
                cursor = cursor.AddMonths(-1);
            }
            if (chain.Count > 0)
            {
                chain.Reverse();
                chains[limit.Category] = chain;
            }
        }

        if (earliestMonth == null) return result;

        var expenses = earliestMonth.Value >= loadedHistoryStart
            ? loadedTransactions.Where(t => t.Amount < 0 && t.Date >= earliestMonth.Value && t.Date < start).ToList()
            : await db.Transactions.AsNoTracking()
                .Where(t => t.UserId == userId && t.Amount < 0 && t.Date >= earliestMonth.Value && t.Date < start)
                .ToListAsync();

        foreach (var (category, chain) in chains)
        {
            var carry = 0m;
            foreach (var (month, monthLimit) in chain)
            {
                var monthEnd = month.AddMonths(1);
                var monthSpent = -expenses
                    .Where(t => t.Date >= month && t.Date < monthEnd && FinanceRules.Category(t.Category).Equals(category, StringComparison.OrdinalIgnoreCase))
                    .Sum(t => t.Amount);
                carry = Math.Max(0m, monthLimit + carry - monthSpent);
            }
            result[category] = carry;
        }

        return result;
    }

    private static List<MerchantSummary> BuildTopMerchants(List<Transaction> current, List<Transaction> previousMonth, decimal totalSpent)
    {
        var previousByMerchant = previousMonth
            .Where(t => t.Amount < 0)
            .GroupBy(t => FinanceRules.MerchantName(t.Description), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => -g.Sum(t => t.Amount), StringComparer.OrdinalIgnoreCase);

        return current
            .Where(t => t.Amount < 0)
            .GroupBy(t => FinanceRules.MerchantName(t.Description), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var displayName = group
                    .GroupBy(t => FinanceRules.MerchantName(t.Description), StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .First().Key;
                var merchantSpent = -group.Sum(t => t.Amount);
                var primaryCategory = group
                    .GroupBy(t => FinanceRules.Category(t.Category), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => -g.Sum(t => t.Amount))
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .First().Key;
                var prevSpent = previousByMerchant.TryGetValue(displayName, out var previous) ? previous : 0m;
                var share = totalSpent > 0 ? decimal.Round(merchantSpent / totalSpent * 100m, 1) : 0m;
                return new MerchantSummary(
                    displayName,
                    displayName,
                    primaryCategory,
                    merchantSpent,
                    group.Count(),
                    share,
                    prevSpent,
                    merchantSpent - prevSpent);
            })
            .OrderByDescending(m => m.Spent)
            .ThenByDescending(m => m.TransactionCount)
            .ThenBy(m => m.Merchant, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static decimal SumCategoryExpenses(List<Transaction> rows, string category) =>
        -rows.Where(t => t.Amount < 0 && FinanceRules.Category(t.Category).Equals(category, StringComparison.OrdinalIgnoreCase))
            .Sum(t => t.Amount);

    private static decimal? SavingsRateOf(decimal income, decimal expenses) =>
        income > 0 ? decimal.Round((income - expenses) / income * 100m, 1) : null;

    private static PeriodDelta Delta(decimal current, decimal previous) =>
        new(previous, current - previous, previous > 0 ? decimal.Round((current - previous) / previous * 100m, 1) : null);

    private static void AddWarning(List<BudgetWarning> warnings, string scope, string? category, decimal limit, decimal spent, int threshold)
    {
        if (limit < 0 || (limit == 0 && spent == 0)) return;
        if (limit > 0 && spent * 100 < limit * threshold) return;
        warnings.Add(new(scope, category, limit, spent, limit > 0 ? decimal.Round(spent / limit * 100, 1) : null,
            spent > limit ? "over" : spent == limit ? "reached" : "approaching"));
    }
}
