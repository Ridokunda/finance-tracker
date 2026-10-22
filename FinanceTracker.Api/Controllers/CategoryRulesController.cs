using System.Security.Claims;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/category-rules")]
public class CategoryRulesController(ApplicationDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue("UserId")!);

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await db.CategorizationRules.AsNoTracking()
        .Where(r => r.UserId == UserId).OrderByDescending(r => r.Pattern.Length).ThenBy(r => r.Id)
        .Select(r => new { r.Id, r.Pattern, r.Category, r.Enabled }).ToListAsync());

    [HttpPost]
    public Task<IActionResult> Create(RuleRequest request) => Save(null, request);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, RuleRequest request) => Save(id, request);

    private async Task<IActionResult> Save(int? id, RuleRequest request)
    {
        var pattern = CategoryRuleMatcher.Normalize(request.Pattern ?? "");
        if (pattern.Length is < 1 or > 200 || string.IsNullOrWhiteSpace(request.Category))
            return BadRequest("Enter a description phrase (1–200 characters) and a category.");
        var rule = id == null ? new CategorizationRule { UserId = UserId } :
            await db.CategorizationRules.SingleOrDefaultAsync(r => r.Id == id && r.UserId == UserId);
        if (rule == null) return NotFound();
        var normalized = pattern.ToUpperInvariant();
        if (await db.CategorizationRules.AnyAsync(r => r.UserId == UserId && r.Id != (id ?? 0) && r.Pattern.ToUpper() == normalized))
            return Conflict("A rule for this phrase already exists. Edit the existing rule.");
        try { rule.Category = FinanceRules.Category(request.Category); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        rule.Pattern = pattern;
        rule.Enabled = request.Enabled;
        if (id == null) db.CategorizationRules.Add(rule);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict("The rules changed concurrently. Reload and try again."); }
        return Ok(new { rule.Id, rule.Pattern, rule.Category, rule.Enabled });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var rule = await db.CategorizationRules.SingleOrDefaultAsync(r => r.Id == id && r.UserId == UserId);
        if (rule == null) return NotFound();
        db.CategorizationRules.Remove(rule);
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record RuleRequest(string? Pattern, string? Category, bool Enabled = true);
}
