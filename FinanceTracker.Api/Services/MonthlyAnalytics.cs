using FinanceTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace FinanceTracker.Api.Services;
public record CategorySummary(string Category, decimal? Limit, decimal Spent, decimal? Remaining, decimal? Progress, string Status);
public record TrendSummary(string Month, decimal Income, decimal Expenses);
public record MonthlySummary(int Year, int Month, bool HasBudget, decimal Budget, decimal Spent, decimal Remaining,
    string Status, bool NeedsReconfirmation, decimal Income, decimal NetCashFlow,
    IReadOnlyList<CategorySummary> Categories, IReadOnlyList<TrendSummary> Trends);
public class MonthlyAnalytics(ApplicationDbContext db)
{
    public async Task<MonthlySummary> Build(int userId, DateTime start)
    {
        var end = start.AddMonths(1);
        var history = start.AddMonths(-5);
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.UserId == userId && t.Date >= history && t.Date < end).ToListAsync();
        var current = transactions.Where(t => t.Date >= start).ToList();
        var budget = await db.MonthlyBudgets.AsNoTracking().Include(b => b.CategoryLimits)
            .SingleOrDefaultAsync(b => b.UserId == userId && b.Year == start.Year && b.Month == start.Month);
        var spent = -current.Where(t => t.Amount < 0).Sum(t => t.Amount);
        var income = current.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var limits = budget?.CategoryLimits ?? [];
        var names = current.Select(t => FinanceRules.Category(t.Category)).Concat(limits.Select(c => c.Category))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
        var categories = names.Select(name => {
            var actual = -current.Where(t => t.Amount < 0 && FinanceRules.Category(t.Category).Equals(name, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount);
            var limit = limits.FirstOrDefault(c => c.Category.Equals(name, StringComparison.OrdinalIgnoreCase))?.LimitAmount;
            return new CategorySummary(name, limit, actual, limit - actual, limit > 0 ? actual / limit * 100 : null,
                limit == null ? "No limit set" : actual > limit ? "Over budget" : "Within budget");
        }).ToList();
        var trends = Enumerable.Range(0, 6).Select(i => {
            var month = history.AddMonths(i);
            var rows = transactions.Where(t => t.Date >= month && t.Date < month.AddMonths(1)).ToList();
            return new TrendSummary(month.ToString("yyyy-MM"), rows.Where(t => t.Amount > 0).Sum(t => t.Amount), -rows.Where(t => t.Amount < 0).Sum(t => t.Amount));
        }).ToList();
        var amount = budget?.LimitAmount ?? 0;
        return new(start.Year, start.Month, budget != null, amount, spent, amount - spent,
            budget == null ? "unset" : spent > amount ? "over" : "ok", budget?.NeedsReconfirmation ?? false, income, income - spent, categories, trends);
    }
}
