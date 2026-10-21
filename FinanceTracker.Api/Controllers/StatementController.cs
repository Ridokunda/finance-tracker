using System.Text.Json;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Controllers;
[ApiController, Authorize, Route("api/statement")]
public class StatementController(ApplicationDbContext db, StatementParser parser, IClassifierClient classifier, MonthlyAnalytics analytics) : ControllerBase
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private int UserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpPost("upload")]
    public IActionResult RetiredUpload() => StatusCode(410, "Direct-save uploads are retired. Use preview, review the rows, then confirm.");

    [HttpPost("preview"), RequestSizeLimit(MaxBytes + 65536), RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> Preview(IFormFile file, [FromForm] string dateColumn = "Date", [FromForm] string descriptionColumn = "Description",
        [FromForm] string amountColumn = "Amount", [FromForm] string dateFormat = "yyyy-MM-dd", [FromForm] string delimiter = "")
    {
        if (file == null || file.Length == 0) return BadRequest("Choose a non-empty statement.");
        if (file.Length > MaxBytes) return StatusCode(413, "Statements must be 10 MB or smaller.");
        ParsedStatement parsed;
        try
        {
            using var stream = file.OpenReadStream();
            parsed = Path.GetExtension(file.FileName).ToLowerInvariant() switch {
                ".csv" => parser.Csv(stream, new(dateColumn, descriptionColumn, amountColumn, dateFormat, delimiter)),
                ".pdf" => parser.Pdf(stream),
                _ => throw new ArgumentException("Only generic CSV and text-based ABSA PDF files are supported.")
            };
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        if (parsed.Rows.Count == 0) return Ok(new { columns = parsed.Columns, notices = parsed.Notices, rows = parsed.Rows });
        var valid = parsed.Rows.Where(r => r.Error == null).ToList();
        var ledger = await db.Transactions.AsNoTracking().Where(t => t.UserId == UserId).Select(t => new { t.Date, t.Description, t.Amount }).ToListAsync();
        var seen = ledger.Select(t => FinanceRules.Fingerprint(t.Date, t.Description, t.Amount)).ToHashSet();
        var categories = await db.Transactions.Where(t => t.UserId == UserId).Select(t => t.Category).Distinct().ToListAsync();
        // One total deadline; after an outage, the remaining rows stay editable and uncategorized.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var unavailable = false;
        foreach (var row in valid)
        {
            row.PossibleDuplicate = !seen.Add(FinanceRules.Fingerprint(row.Date!.Value, row.Description, row.Amount!.Value));
            row.Included = !row.PossibleDuplicate;
            if (!unavailable)
            {
                var suggestion = await classifier.Suggest(row.Description, deadline.Token);
                unavailable = suggestion == null;
                if (suggestion != null) row.Category = FinanceRules.Category(suggestion, categories);
            }
        }
        if (unavailable) parsed.Notices.Add("Classification service unavailable or timed out. Review uncategorized rows and choose categories manually.");
        var batch = new ImportBatch { UserId = UserId, Rows = parsed.Rows, NoticesJson = JsonSerializer.Serialize(parsed.Notices), StatementStart = parsed.Start, StatementEnd = parsed.End };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();
        return Ok(View(batch, parsed.Columns));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var batch = await db.ImportBatches.AsNoTracking().Include(b => b.Rows).SingleOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (batch == null) return NotFound();
        if (batch.ConfirmedAt == null && batch.ExpiresAt <= DateTime.UtcNow) return StatusCode(410, "Preview expired. Upload the source again.");
        return Ok(View(batch));
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, ConfirmRequest request)
    {
        var batch = await db.ImportBatches.Include(b => b.Rows).SingleOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (batch == null) return NotFound();
        if (batch.ConfirmedAt != null) return Ok(new { id, count = batch.ConfirmedCount });
        if (batch.ExpiresAt <= DateTime.UtcNow) return StatusCode(410, "Preview expired. Upload the source again.");
        if (request.Rows == null || request.Rows.Count != batch.Rows.Count || request.Rows.Select(r => r.Id).Distinct().Count() != batch.Rows.Count || request.Rows.Any(r => batch.Rows.All(b => b.Id != r.Id)))
            return BadRequest("Submit one review decision for every preview row.");
        var names = await db.Transactions.Where(t => t.UserId == UserId).Select(t => t.Category).Distinct().ToListAsync();
        var ledger = await db.Transactions.AsNoTracking().Where(t => t.UserId == UserId && (t.ImportRowId == null || !db.ImportRows.Any(r => r.Id == t.ImportRowId && r.ImportBatchId == id))).Select(t => new { t.Date, t.Description, t.Amount }).ToListAsync();
        var seen = ledger.Select(t => FinanceRules.Fingerprint(t.Date, t.Description, t.Amount)).ToHashSet();
        var additions = new List<Transaction>();
        foreach (var row in batch.Rows.OrderBy(r => r.RowNumber))
        {
            var decision = request.Rows.Single(r => r.Id == row.Id);
            if (decision.Included && (row.Error != null || row.Date == null || row.Amount == null)) return BadRequest("Invalid rows must be excluded. Correct the source and upload again to include them.");
            string category;
            try { category = FinanceRules.Category(decision.Category, names); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            if (decision.Included)
            {
                var duplicate = !seen.Add(FinanceRules.Fingerprint(row.Date!.Value, row.Description, row.Amount!.Value));
                if ((row.PossibleDuplicate || duplicate) && !decision.KeepDuplicate) return Conflict("A selected row matches an existing or repeated purchase. Explicitly choose Keep duplicate or exclude it.");
                additions.Add(new Transaction { UserId = UserId, Date = row.Date.Value.Date, Description = row.Description, Amount = row.Amount.Value, Category = category, ImportRowId = row.Id });
            }
            row.Category = category;
            row.Included = decision.Included;
        }
        if (additions.Count == 0) return BadRequest("Select at least one valid row to confirm.");
        db.Transactions.AddRange(additions);
        batch.ConfirmedAt = DateTime.UtcNow;
        batch.ConfirmedCount = additions.Count;
        // EF wraps this entire SaveChanges in one transaction. Rowversion + unique provenance handle concurrent retries.
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var existing = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
            if (existing?.ConfirmedAt != null) return Ok(new { id, count = existing.ConfirmedCount });
            return Conflict("The preview changed concurrently. Reload it and try again.");
        }
        return Ok(new { id, count = batch.ConfirmedCount });
    }
    [HttpGet("budget/{year:int}/{month:int}")]
    public async Task<IActionResult> GetMonthlyBudget(int year, int month)
    {
        try { var s = await analytics.Build(UserId, FinanceRules.Month(year, month)); return Ok(new { s.Year, s.Month, LimitAmount = s.Budget, TotalExpenses = s.Spent, s.Remaining }); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }
    private static object View(ImportBatch batch, string[]? columns = null) => new {
        batch.Id, batch.ExpiresAt, batch.ConfirmedAt, batch.ConfirmedCount, batch.StatementStart, batch.StatementEnd,
        notices = JsonSerializer.Deserialize<string[]>(batch.NoticesJson), columns = columns ?? [], rows = batch.Rows.OrderBy(r => r.RowNumber)
    };
    public record RowDecision(int Id, bool Included, string Category, bool KeepDuplicate = false);
    public record ConfirmRequest(List<RowDecision>? Rows);
}
