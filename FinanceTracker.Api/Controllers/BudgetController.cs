using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FinanceTracker.Api.Controllers;
[ApiController, Authorize, Route("api/budget")]
public class BudgetController(ApplicationDbContext db, MonthlyAnalytics analytics) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirst("UserId")!.Value);
    [HttpGet("current"), HttpGet("summary"), HttpGet("analytics")]
    public async Task<IActionResult> GetBudgetSummary(int? year = null, int? month = null)
    {
        try { return Ok(await analytics.Build(UserId, FinanceRules.Month(year, month))); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }
    [HttpPost("set")]
    public async Task<IActionResult> SetBudget(SetBudgetRequest request)
    {
        DateTime start;
        try { start = FinanceRules.Month(request.Year, request.Month); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        if (request.LimitAmount < 0 || !FinanceRules.ValidMoney(request.LimitAmount)) return BadRequest("Enter a non-negative limit with at most two decimal places.");
        var budget = await db.MonthlyBudgets.Include(b => b.CategoryLimits).SingleOrDefaultAsync(b => b.UserId == UserId && b.Year == start.Year && b.Month == start.Month);
        var existingNames = await db.Transactions.Where(t => t.UserId == UserId).Select(t => t.Category).Distinct().ToListAsync();
        var limits = request.CategoryLimits ?? budget?.CategoryLimits.Select(c => new LimitRequest(c.Category, c.LimitAmount)).ToList() ?? [];
        try { limits = limits.Select(c => c with { Category = FinanceRules.Category(c.Category, existingNames) }).ToList(); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        if (limits.Any(c => c.LimitAmount < 0 || !FinanceRules.ValidMoney(c.LimitAmount)) || limits.Sum(c => c.LimitAmount) > request.LimitAmount)
            return BadRequest("Category limits must be non-negative and their sum cannot exceed the monthly limit.");
        if (limits.Select(c => c.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count() != limits.Count)
            return BadRequest("Each category can have only one limit.");
        if (budget == null)
        {
            budget = new MonthlyBudget { UserId = UserId, Year = start.Year, Month = start.Month };
            db.MonthlyBudgets.Add(budget);
        }
        // Serialize all allocation edits through the monthly rowversion, even if the overall amount is unchanged.
        if (budget.Id != 0) db.Entry(budget).Property(b => b.LimitAmount).IsModified = true;
        budget.LimitAmount = request.LimitAmount;
        budget.NeedsReconfirmation = false;
        foreach (var old in budget.CategoryLimits.ToList())
        {
            var replacement = limits.FirstOrDefault(c => c.Category.Equals(old.Category, StringComparison.OrdinalIgnoreCase));
            if (replacement == null) db.CategoryLimits.Remove(old);
            else old.LimitAmount = replacement.LimitAmount;
        }
        foreach (var limit in limits.Where(c => !budget.CategoryLimits.Any(old => old.Category.Equals(c.Category, StringComparison.OrdinalIgnoreCase))))
            budget.CategoryLimits.Add(new CategoryLimit { Category = limit.Category, LimitAmount = limit.LimitAmount });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict("This month changed concurrently. Reload and try again."); }
        return Ok(await analytics.Build(UserId, start));
    }
    [HttpPost("copy-previous")]
    public async Task<IActionResult> CopyPrevious(MonthRequest request)
    {
        DateTime start;
        try { start = FinanceRules.Month(request.Year, request.Month); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        var previous = start.AddMonths(-1);
        var source = await db.MonthlyBudgets.AsNoTracking().Include(b => b.CategoryLimits).SingleOrDefaultAsync(b => b.UserId == UserId && b.Year == previous.Year && b.Month == previous.Month);
        if (source == null) return NotFound("The previous month has no budget to copy.");
        if (source.NeedsReconfirmation) return Conflict("Reconfirm the previous month's limit before copying it.");
        return await SetBudget(new SetBudgetRequest { Year = start.Year, Month = start.Month, LimitAmount = source.LimitAmount,
            CategoryLimits = source.CategoryLimits.Select(c => new LimitRequest(c.Category, c.LimitAmount)).ToList() });
    }
    public record MonthRequest(int? Year, int? Month);
    public record LimitRequest(string Category, decimal LimitAmount);
    public class SetBudgetRequest
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public decimal LimitAmount { get; set; }
        public List<LimitRequest>? CategoryLimits { get; set; }
    }
}
