using FinanceTracker.Api.Models;

namespace FinanceTracker.Api.Services;

public static class BillSchedule
{
    public static readonly string[] Frequencies = ["weekly", "fortnightly", "monthly", "yearly"];

    // Always calculate from the original anchor, so Jan 31 -> Feb 28 -> Mar 31.
    public static IEnumerable<DateTime> Dates(RecurringBill bill, DateTime from, DateTime to)
    {
        var anchor = bill.FirstDueDate.Date;
        var end = bill.EndDate?.Date < to.Date ? bill.EndDate.Value.Date : to.Date;
        if (end < anchor || from.Date > end) yield break;
        var monthly = bill.Frequency is "monthly" or "yearly";
        var interval = bill.Frequency is "fortnightly" ? 14 : 7;
        var step = monthly ? Math.Max(0, ((from.Year - anchor.Year) * 12 + from.Month - anchor.Month) / (bill.Frequency == "yearly" ? 12 : 1))
            : Math.Max(0, (from.Date - anchor).Days / interval);
        while (true)
        {
            DateTime candidate;
            try { candidate = monthly ? anchor.AddMonths(step * (bill.Frequency == "yearly" ? 12 : 1)) : anchor.AddDays((long)step * interval); }
            catch (ArgumentOutOfRangeException) { yield break; }
            if (candidate > end) yield break;
            if (candidate >= from.Date) yield return candidate;
            step++;
        }
    }

    public static bool IsDue(RecurringBill bill, DateTime date) => Dates(bill, date.Date, date.Date).Any();
}
