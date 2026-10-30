using FinanceTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Services;

public record RecordedBillExpense(string MatchText, DateTime Date, decimal Amount);

public static class RecurringPaymentMatcher
{
    public static Task<List<RecordedBillExpense>> Load(ApplicationDbContext db, int userId) =>
        (from payment in db.BillPayments.AsNoTracking()
         join bill in db.RecurringBills on payment.RecurringBillId equals bill.Id
         join transaction in db.Transactions on payment.TransactionId equals transaction.Id
         where bill.UserId == userId && transaction.UserId == userId && transaction.Amount < 0
         select new RecordedBillExpense(bill.MatchText, transaction.Date, transaction.Amount)).ToListAsync();

    public static bool Matches(IEnumerable<RecordedBillExpense> payments, DateTime date, string description, decimal amount)
    {
        var text = CategoryRuleMatcher.Normalize(description);
        return payments.Any(p => p.Amount == amount && Math.Abs((p.Date.Date - date.Date).Days) <= 7
            && text.Contains(p.MatchText, StringComparison.OrdinalIgnoreCase));
    }
}
