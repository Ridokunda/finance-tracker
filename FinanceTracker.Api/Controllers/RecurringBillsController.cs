using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Controllers;

[ApiController, Authorize, Route("api/recurring-bills")]
public class RecurringBillsController(ApplicationDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var bills = await db.RecurringBills.AsNoTracking().Where(b => b.UserId == UserId && !b.Archived).OrderBy(b => b.Name).ToListAsync();
        return Ok(bills.Select(View));
    }

    [HttpPost]
    public Task<IActionResult> Create(BillRequest request) => Save(null, request);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, BillRequest request) => Save(id, request);

    private async Task<IActionResult> Save(int? id, BillRequest request)
    {
        var name = CategoryRuleMatcher.Normalize(request.Name ?? "");
        var matchText = CategoryRuleMatcher.Normalize(request.MatchText ?? name);
        if (name.Length is < 1 or > 120 || matchText.Length is < 1 or > 200)
            return BadRequest("Enter a bill name (1–120 characters) and a statement matching phrase (1–200 characters).");
        if (request.Amount <= 0 || !FinanceRules.ValidMoney(request.Amount)) return BadRequest("Enter a positive amount with at most two decimal places.");
        var frequency = request.Frequency?.ToLowerInvariant() ?? "";
        if (!BillSchedule.Frequencies.Contains(frequency)) return BadRequest("Choose weekly, fortnightly, monthly or yearly.");
        if (!ValidDate(request.FirstDueDate) || (request.EndDate != null && (!ValidDate(request.EndDate.Value) || request.EndDate.Value.Date < request.FirstDueDate.Date)))
            return BadRequest("Choose valid dates between 1900 and 9998. The end date must be on or after the first due date.");
        string category;
        try { category = FinanceRules.Category(request.Category); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        var bill = id == null ? new RecurringBill { UserId = UserId } : await db.RecurringBills.SingleOrDefaultAsync(b => b.Id == id && b.UserId == UserId && !b.Archived);
        if (bill == null) return NotFound();
        bill.Name = name; bill.MatchText = matchText; bill.Amount = request.Amount; bill.Category = category;
        bill.Frequency = frequency; bill.FirstDueDate = request.FirstDueDate.Date; bill.EndDate = request.EndDate?.Date; bill.Enabled = request.Enabled;
        if (id == null) db.RecurringBills.Add(bill);
        await db.SaveChangesAsync();
        return Ok(View(bill));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Archive(int id)
    {
        var bill = await db.RecurringBills.SingleOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (bill == null) return NotFound();
        bill.Archived = true; bill.Enabled = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("occurrences")]
    public async Task<IActionResult> Occurrences(int? year = null, int? month = null)
    {
        DateTime start;
        try { start = FinanceRules.Month(year, month); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        var end = start.AddMonths(1).AddDays(-1);
        var bills = await db.RecurringBills.AsNoTracking().Where(b => b.UserId == UserId).ToListAsync();
        var ids = bills.Select(b => b.Id).ToList();
        // Ignore the transaction soft-delete filter here so a deleted payment remains recoverable.
        var payments = await db.BillPayments.IgnoreQueryFilters().AsNoTracking().Include(p => p.Transaction)
            .Where(p => ids.Contains(p.RecurringBillId) && p.DueDate >= start && p.DueDate <= end && p.Transaction.UserId == UserId).ToListAsync();
        var occurrences = new List<BillOccurrence>();
        foreach (var bill in bills)
        {
            var paid = payments.Where(p => p.RecurringBillId == bill.Id).ToDictionary(p => p.DueDate.Date);
            var dates = (bill.Enabled && !bill.Archived ? BillSchedule.Dates(bill, start, end) : [])
                .Concat(paid.Keys).Distinct().OrderBy(d => d);
            foreach (var date in dates)
            {
                paid.TryGetValue(date, out var payment);
                var status = payment == null ? (date < FinanceRules.Today ? "overdue" : date == FinanceRules.Today ? "due" : "upcoming") :
                    payment.Transaction.DeletedAt != null ? "payment-deleted" : payment.Transaction.Amount >= 0 ? "payment-changed" : "paid";
                occurrences.Add(new(bill.Id, bill.Name, bill.Category, bill.Amount, date, status, payment?.TransactionId,
                    payment?.Transaction.Date, payment == null ? null : Math.Abs(payment.Transaction.Amount)));
            }
        }
        return Ok(occurrences.OrderBy(o => o.DueDate).ThenBy(o => o.Name));
    }

    [HttpGet("{id:int}/payment-candidates")]
    public async Task<IActionResult> Candidates(int id, DateTime date, decimal? amount = null)
    {
        var bill = await db.RecurringBills.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && b.UserId == UserId && !b.Archived);
        if (bill == null) return NotFound();
        var value = amount ?? bill.Amount;
        if (!ValidDate(date) || value <= 0 || !FinanceRules.ValidMoney(value)) return BadRequest("Choose a valid payment date and positive amount.");
        return Ok((await FindCandidates(bill, date.Date, value)).Select(t => new { t.Id, t.Date, t.Description, t.Amount, t.Category }));
    }

    [HttpPost("{id:int}/payments")]
    public async Task<IActionResult> Pay(int id, PaymentRequest request)
    {
        var bill = await db.RecurringBills.SingleOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (bill == null) return NotFound();
        if (!ValidDate(request.DueDate)) return BadRequest("Choose a valid scheduled due date.");
        var due = request.DueDate.Date;
        var existing = await Payment(id, due);
        if (existing != null) return PaymentResult(existing);
        if (!bill.Enabled || bill.Archived || !BillSchedule.IsDue(bill, due)) return Conflict("This bill is paused, archived or no longer scheduled for that date. Reload the schedule.");
        Transaction transaction;
        if (request.TransactionId != null)
        {
            var selected = await db.Transactions.SingleOrDefaultAsync(t => t.Id == request.TransactionId && t.UserId == UserId && t.Amount < 0);
            if (selected == null) return NotFound("Choose an active expense belonging to you.");
            if (await db.BillPayments.AnyAsync(p => p.TransactionId == selected.Id)) return Conflict("This expense is already linked to a scheduled payment.");
            transaction = selected;
        }
        else
        {
            var date = request.PaymentDate?.Date ?? due;
            var amount = request.Amount ?? bill.Amount;
            if (!ValidDate(date) || amount <= 0 || !FinanceRules.ValidMoney(amount)) return BadRequest("Choose a valid payment date and positive amount.");
            if (!request.ConfirmSeparateExpense && (await FindCandidates(bill, date, amount)).Count > 0)
                return Conflict("An existing expense may match this payment. Link it, or confirm that this is a separate expense.");
            transaction = new Transaction { UserId = UserId, Date = date, Description = bill.Name, Category = bill.Category, Amount = -amount };
        }
        var payment = new BillPayment { RecurringBillId = id, DueDate = due, Transaction = transaction };
        db.BillPayments.Add(payment);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await Payment(id, due);
            return existing != null ? PaymentResult(existing) : Conflict("The payment changed concurrently. Reload and try again.");
        }
        return PaymentResult(payment);
    }

    [HttpDelete("{id:int}/payments")]
    public async Task<IActionResult> Unlink(int id, DateTime dueDate)
    {
        if (!await db.RecurringBills.AnyAsync(b => b.Id == id && b.UserId == UserId)) return NotFound();
        var payment = await db.BillPayments.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.RecurringBillId == id && p.DueDate == dueDate.Date);
        if (payment == null) return NotFound();
        db.BillPayments.Remove(payment);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<BillPayment?> Payment(int billId, DateTime due) => db.BillPayments.IgnoreQueryFilters().Include(p => p.Transaction)
        .SingleOrDefaultAsync(p => p.RecurringBillId == billId && p.DueDate == due && p.Transaction.UserId == UserId);

    private IActionResult PaymentResult(BillPayment payment) => payment.Transaction.DeletedAt != null || payment.Transaction.Amount >= 0
        ? Conflict("The linked payment was deleted or changed to income. Restore/correct it in Transactions, or unlink it before recording a replacement.")
        : Ok(new { transactionId = payment.TransactionId, payment.DueDate });

    private async Task<List<Transaction>> FindCandidates(RecurringBill bill, DateTime date, decimal amount)
    {
        var start = date.AddDays(-7); var end = date.AddDays(7);
        var rows = await db.Transactions.AsNoTracking().Where(t => t.UserId == UserId && t.Amount < 0 && t.Date >= start && t.Date <= end
            && !db.BillPayments.Any(p => p.TransactionId == t.Id)).OrderByDescending(t => t.Date).ToListAsync();
        return rows.Where(t => t.Amount == -amount || CategoryRuleMatcher.Normalize(t.Description).Contains(bill.MatchText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool ValidDate(DateTime date) => date.Year is >= 1900 and <= 9998;
    private static object View(RecurringBill bill)
    {
        var from = bill.FirstDueDate > FinanceRules.Today ? bill.FirstDueDate : FinanceRules.Today;
        DateTime? next = bill.Enabled && !bill.Archived ? BillSchedule.Dates(bill, from, new DateTime(Math.Min(9998, from.Year + 2), 12, 31)).Select(d => (DateTime?)d).FirstOrDefault() : null;
        return new { bill.Id, bill.Name, bill.MatchText, bill.Amount, bill.Category, bill.Frequency, bill.FirstDueDate, bill.EndDate, bill.Enabled, nextDueDate = next };
    }

    public record BillRequest(string? Name, string? MatchText, decimal Amount, string? Category, string? Frequency, DateTime FirstDueDate, DateTime? EndDate = null, bool Enabled = true);
    public record PaymentRequest(DateTime DueDate, DateTime? PaymentDate = null, decimal? Amount = null, int? TransactionId = null, bool ConfirmSeparateExpense = false);
    public record BillOccurrence(int BillId, string Name, string Category, decimal Amount, DateTime DueDate, string Status, int? TransactionId, DateTime? PaymentDate, decimal? PaidAmount);
}
