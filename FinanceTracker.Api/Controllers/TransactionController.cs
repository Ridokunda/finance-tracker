using System.Security.Claims;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/[controller]")]
public class TransactionController(ApplicationDbContext db, IClassifierClient classifier) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue("UserId")!);

    [HttpGet]
    public IActionResult Get([FromQuery] string? type, [FromQuery] string? category,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null, [FromQuery] bool deleted = false)
    {
        var query = deleted ? db.Transactions.IgnoreQueryFilters().Where(t => t.DeletedAt != null) : db.Transactions;
        query = query.AsNoTracking().Where(t => t.UserId == UserId);
        if (from > to) return BadRequest("Start date must be on or before end date.");
        if (from != null) query = query.Where(t => t.Date >= from.Value.Date);
        if (to != null) query = query.Where(t => t.Date <= to.Value.Date);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(t => t.Description.Contains(search.Trim()));
        if (type?.Equals("income", StringComparison.OrdinalIgnoreCase) == true) query = query.Where(t => t.Amount > 0);
        if (type?.Equals("expense", StringComparison.OrdinalIgnoreCase) == true) query = query.Where(t => t.Amount < 0);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalized = category.ToLowerInvariant();
            query = query.Where(t => t.Category.ToLower() == normalized);
        }
        return Ok(query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Date, t.Description, t.Amount, t.Category, t.DeletedAt }).ToList());
    }

    [HttpPost("classify")]
    public async Task<IActionResult> Classify([FromBody] ClassifyTransactionRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000)
            return BadRequest("Enter a description of 1–2000 characters.");
        var rules = await db.CategorizationRules.AsNoTracking().Where(r => r.UserId == UserId && r.Enabled).ToListAsync();
        var rule = CategoryRuleMatcher.Match(rules, request.Description);
        if (rule != null) return Ok(new { rule.Category, source = "rule", matchedPattern = rule.Pattern });
        var category = await classifier.Suggest(request.Description.Trim(), HttpContext.RequestAborted);
        return category == null
            ? StatusCode(503, new { message = "The classification service is unavailable. Choose a category manually." })
            : Ok(new { category, source = "model" });
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateTransactionRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000)
            return BadRequest("Enter a description of 1–2000 characters.");
        var entity = new Transaction { UserId = UserId, Date = request.Date == default ? FinanceRules.Today : request.Date.Date,
            Description = request.Description.Trim() };
        try
        {
            entity.Amount = FinanceRules.TransactionAmount(request.Amount, request.Type);
            entity.Category = NormalizeCategory(request.Category);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        db.Transactions.Add(entity);
        db.SaveChanges();
        return Ok(new { entity.Id, entity.Date, entity.Description, entity.Amount, entity.Category });
    }

    [HttpPut("{id:int}")]
    public IActionResult Update(int id, [FromBody] UpdateTransactionRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000)
            return BadRequest("Enter a description of 1–2000 characters.");
        var entity = db.Transactions.SingleOrDefault(t => t.Id == id && t.UserId == UserId);
        if (entity == null) return NotFound("Transaction not found.");
        decimal amount; string category;
        try
        {
            amount = FinanceRules.TransactionAmount(request.Amount, request.Type);
            category = NormalizeCategory(request.Category);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        entity.Date = request.Date == default ? entity.Date : request.Date.Date;
        entity.Description = request.Description.Trim();
        entity.Amount = amount;
        entity.Category = category;
        db.SaveChanges();
        return Ok(new { entity.Id, entity.Date, entity.Description, entity.Amount, entity.Category });
    }

    [HttpPost("bulk-category")]
    public async Task<IActionResult> BulkCategory(BulkCategoryRequest request)
    {
        if (!ValidIds(request.Ids)) return BadRequest("Select between 1 and 5,000 distinct transactions.");
        if (string.IsNullOrWhiteSpace(request.Category)) return BadRequest("Choose a category.");
        string category;
        try { category = NormalizeCategory(request.Category); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        var rows = await db.Transactions.Where(t => t.UserId == UserId && request.Ids!.Contains(t.Id)).ToListAsync();
        if (rows.Count != request.Ids!.Count) return NotFound("Some selected transactions are unavailable. Reload and try again.");
        foreach (var row in rows) row.Category = category;
        await db.SaveChangesAsync();
        return Ok(new { count = rows.Count });
    }

    [HttpPost("delete")]
    public Task<IActionResult> Delete(TransactionIdsRequest request) => SetDeleted(request, true);

    [HttpPost("restore")]
    public Task<IActionResult> Restore(TransactionIdsRequest request) => SetDeleted(request, false);

    private async Task<IActionResult> SetDeleted(TransactionIdsRequest request, bool deleted)
    {
        if (!ValidIds(request.Ids)) return BadRequest("Select between 1 and 5,000 distinct transactions.");
        // Include deleted rows for retry-safe delete/restore, while always checking ownership.
        var rows = await db.Transactions.IgnoreQueryFilters()
            .Where(t => t.UserId == UserId && request.Ids!.Contains(t.Id)).ToListAsync();
        if (rows.Count != request.Ids!.Count) return NotFound("Some selected transactions are unavailable. Reload and try again.");
        var now = DateTime.UtcNow;
        foreach (var row in rows) row.DeletedAt = deleted ? row.DeletedAt ?? now : null;
        await db.SaveChangesAsync();
        return Ok(new { ids = rows.Select(t => t.Id), count = rows.Count });
    }

    private static bool ValidIds(List<int>? ids) => ids is { Count: > 0 and <= 5000 }
        && ids.All(id => id > 0) && ids.Distinct().Count() == ids.Count;

    private string NormalizeCategory(string? value) => FinanceRules.Category(value,
        db.Transactions.Where(t => t.UserId == UserId).Select(t => t.Category)
            .Concat(db.CategorizationRules.Where(r => r.UserId == UserId).Select(r => r.Category)).Distinct().ToList());

    [HttpGet("net-cash-flow")]
    public IActionResult GetBalance() => Ok(new { netCashFlow = db.Transactions.Where(t => t.UserId == UserId).Sum(t => t.Amount) });

    [HttpGet("categories")]
    public IActionResult Categories()
    {
        var existing = db.Transactions.Where(t => t.UserId == UserId).Select(t => t.Category).Distinct().ToList();
        var budgetNames = db.MonthlyBudgets.Where(b => b.UserId == UserId).SelectMany(b => b.CategoryLimits).Select(c => c.Category).ToList();
        var ruleNames = db.CategorizationRules.Where(r => r.UserId == UserId).Select(r => r.Category).ToList();
        return Ok(FinanceRules.Categories.Concat(existing).Concat(budgetNames).Concat(ruleNames)
            .Select(c => FinanceRules.Category(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c));
    }

    public class CreateTransactionRequest
    {
        public DateTime Date { get; set; }
        public string Description { get; set; } = "";
        public string? Category { get; set; }
        public decimal Amount { get; set; }
        public string? Type { get; set; }
    }
    public class UpdateTransactionRequest : CreateTransactionRequest { }
    public class ClassifyTransactionRequest { public string Description { get; set; } = ""; }
    public record TransactionIdsRequest(List<int>? Ids);
    public record BulkCategoryRequest(List<int>? Ids, string? Category);
}
