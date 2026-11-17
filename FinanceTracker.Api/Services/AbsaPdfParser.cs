using System.Globalization;
using System.Text.RegularExpressions;
using FinanceTracker.Api.Models;
using UglyToad.PdfPig.Content;

namespace FinanceTracker.Api.Services;

// Coordinates come from the table headers on each page, never from PDF stream order.
internal static class AbsaPdfParser
{
    private static readonly string[] Dates = ["d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d MMM yyyy", "dd MMM yyyy"];
    private const string PeriodDate = @"(?:\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2} [A-Za-z]{3} \d{4})";
    private record Layout(double Date, double Description, double Debit, double Credit, double Balance,
        double? DebitRight = null, double? CreditRight = null, double? BalanceRight = null)
    {
        public bool Cheque => DebitRight != null;
    }

    public static ParsedStatement Parse(List<List<Word>> pages)
    {
        if (!pages.SelectMany(p => p).Any(w => w.Text.Equals("ABSA", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Unsupported PDF. Only supported text-based ABSA transaction tables are accepted.");
        var rows = new List<ImportRow>();
        var notices = new List<string>();
        DateTime? start = null, end = null;
        decimal? debitTotal = null, creditTotal = null, openingBalance = null, lastBalance = null;
        var balanceChecks = 0;
        var balanceGaps = false;
        var lineNumber = 0;
        foreach (var words in pages)
        {
            Layout? layout = null;
            ImportRow? previous = null;
            foreach (var line in Lines(words))
            {
                lineNumber++;
                var text = string.Join(" ", line.Select(w => w.Text));
                var period = Regex.Match(text, $@"^(?:Statement (?:period|from)\s+)?(?<start>{PeriodDate})\s+to\s+(?<end>{PeriodDate})$", RegexOptions.IgnoreCase);
                if (period.Success && TryDate(period.Groups["start"].Value, out var first) && TryDate(period.Groups["end"].Value, out var last))
                {
                    if (first > last || (start != null && (start != first || end != last)))
                        throw new ArgumentException("The PDF contains an invalid or conflicting statement period.");
                    start = first; end = last;
                }
                var header = Header(line);
                if (header != null) { layout = header; previous = null; continue; }
                var total = Regex.Match(text, @"^Total\s+(?<kind>debits|credits)\s+(?<amount>[\d, .]+)$", RegexOptions.IgnoreCase);
                if (total.Success && TryMoney(total.Groups["amount"].Value, out var value))
                {
                    if (total.Groups["kind"].Value.Equals("debits", StringComparison.OrdinalIgnoreCase)) debitTotal = value;
                    else creditTotal = value;
                    previous = null;
                    continue;
                }
                if (Regex.IsMatch(text, @"^(?:Our Privacy\b|YOUR PRICING PLAN\b|CREDIT INTEREST\b|\*\s*=\s*VAT\b|Page\b|Authorised\b|CSP\d)", RegexOptions.IgnoreCase))
                { layout = null; previous = null; continue; }
                if (layout == null) continue;

                string Cell(double left, double right) => string.Join(" ", line.Where(w => w.BoundingBox.Left >= left - 4 && w.BoundingBox.Left < right - 4).Select(w => w.Text));
                var dateText = Cell(layout.Date, layout.Description);
                // On cheque statements the first header word is Transaction, not Description.
                // Continuations can extend into the Charge column, including numeric fee references.
                var debitCell = layout.Cheque ? MoneyCell(line, layout.DebitRight!.Value) : null;
                var creditCell = layout.Cheque ? MoneyCell(line, layout.CreditRight!.Value) : null;
                var balanceCell = layout.Cheque ? MoneyCell(line, layout.BalanceRight!.Value) : null;
                var description = string.Join(" ", line.Where(w => w.BoundingBox.Left >= layout.Description - 4
                    && w.BoundingBox.Left < layout.Debit - 4 && !(debitCell?.Words.Contains(w) ?? false)).Select(w => w.Text));
                var debitText = debitCell?.Text ?? Cell(layout.Debit, layout.Credit);
                var creditText = creditCell?.Text ?? Cell(layout.Credit, layout.Balance);
                var balanceText = balanceCell?.Text ?? Cell(layout.Balance, double.MaxValue);

                const string balanceLabel = @"^(?:Bal(?:ance)? (?:Brought|Carried) Forward|Opening balance|Closing balance|Balance brought|Balance carried)\b";
                if (Regex.IsMatch(description, balanceLabel, RegexOptions.IgnoreCase) || Regex.IsMatch(text, balanceLabel, RegexOptions.IgnoreCase))
                {
                    if (TryMoney(balanceText, out var brought, signed: true))
                    {
                        if (lastBalance != null && lastBalance != brought) balanceGaps = true;
                        if (rows.Count == 0) openingBalance = brought;
                        lastBalance = brought;
                    }
                    else if (layout.Cheque) balanceGaps = true;
                    previous = null;
                    continue;
                }

                if (dateText.Length == 0)
                {
                    // A continuation must be entirely within the description region. Never create
                    // transactions from footer text or use a reference amount as a debit/credit.
                    if (previous != null && line.All(w => w.BoundingBox.Left >= layout.Description - 4 && w.BoundingBox.Right < layout.Debit - 4))
                        previous.Description += " " + string.Join(" ", line.Select(w => w.Text));
                    else if (debitText != "" || creditText != "")
                    {
                        previous = new ImportRow { RowNumber = lineNumber, Description = description, Error = "Amount without a readable transaction date. Correct the source and preview again." };
                        Add(previous);
                        if (layout.Cheque) balanceGaps = true;
                    }
                    continue;
                }

                var row = new ImportRow { RowNumber = lineNumber, Description = description };
                if (TryDate(dateText, out var date)) row.Date = date;
                else row.Error = "Unreadable transaction date; expected day/month/year or an ISO date.";
                if (TryMoney(debitText, out var debit) && debit > 0 && creditText == "") row.Amount = -debit;
                else if (TryMoney(creditText, out var credit) && credit > 0 && debitText == "") row.Amount = credit;
                else if (debitText == "" && creditText == "")
                    row.Error = Join(row.Error, "Balance-only entry has no debit or credit amount and is excluded.");
                else row.Error = Join(row.Error, "Could not determine a single positive debit or credit amount.");

                if (layout.Cheque)
                {
                    // Charge is a VAT marker, not a second expense. Strip it only from dated rows.
                    row.Description = Regex.Replace(row.Description, @"\s+\*\s*$", "");
                    if (TryMoney(balanceText, out var balance, signed: true))
                    {
                        if (lastBalance != null && row.Amount != null)
                        {
                            balanceChecks++;
                            if (lastBalance + row.Amount != balance)
                            {
                                row.Error = Join(row.Error, "Running balance does not match this debit or credit; check the statement.");
                                balanceGaps = true;
                            }
                        }
                        else if (row.Amount == null && lastBalance == balance && debitText == "" && creditText == "")
                            row.Amount = 0; // Visible in review, always excluded from confirmation.
                        else balanceGaps = true;
                        lastBalance = balance;
                    }
                    else
                    {
                        row.Error = Join(row.Error, "Unreadable running balance.");
                        lastBalance = null;
                        balanceGaps = true;
                    }
                }
                Add(row);
                previous = row;
            }
        }

        if (rows.Count == 0) throw new ArgumentException("Unsupported ABSA PDF layout: no transactions could be read. Use a CSV export with explicit columns.");
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Description) || row.Description.Length > 2000)
                row.Error = Join(row.Error, "Description is required and must be at most 2000 characters.");
            if (row.Date != null && ((start != null && row.Date < start) || (end != null && row.Date > end)))
                row.Error = Join(row.Error, "Date falls outside the statement period.");
            row.Included = row.Error == null;
        }
        var expenses = -rows.Where(r => r.Amount < 0).Sum(r => r.Amount ?? 0);
        var income = rows.Where(r => r.Amount > 0).Sum(r => r.Amount ?? 0);
        if (debitTotal != null && expenses != debitTotal) notices.Add("Reconciliation mismatch: parsed expenses do not match statement total debits.");
        if (creditTotal != null && income != creditTotal) notices.Add("Reconciliation mismatch: parsed income does not match statement total credits.");
        if (balanceGaps) notices.Add("Running balance reconciliation is incomplete or has a mismatch. Review excluded rows and check the source statement.");
        else if (openingBalance != null && lastBalance != null && balanceChecks > 0 && openingBalance + income - expenses == lastBalance)
            notices.Add("Running balances reconcile from the opening balance to the final transaction. Review all rows before confirming.");
        else if (debitTotal == null && creditTotal == null) notices.Add("No recognized statement totals or complete running balances were available for reconciliation.");
        if (rows.Any(r => r.Amount == 0)) notices.Add("Balance-only entries with no monetary movement are shown for review and excluded from import.");
        if (start == null) notices.Add("No recognized statement period was available. Check transaction dates before confirming.");
        return new(rows, notices, [], start, end);

        void Add(ImportRow row)
        {
            if (rows.Count >= StatementParser.MaxRows) throw new ArgumentException($"Statements are limited to {StatementParser.MaxRows} rows.");
            rows.Add(row);
        }
    }

    private static Layout? Header(List<Word> line)
    {
        Word? Find(string name) => line.FirstOrDefault(w => w.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        var date = Find("Date"); var desc = Find("Description"); var debit = Find("Debit"); var credit = Find("Credit"); var balance = Find("Balance");
        if (date == null || desc == null || debit == null || credit == null) return null;
        var description = Find("Transaction") ?? desc;
        if (!(date.BoundingBox.Left < description.BoundingBox.Left && description.BoundingBox.Left < debit.BoundingBox.Left && debit.BoundingBox.Left < credit.BoundingBox.Left)) return null;
        var debitAmount = line.FirstOrDefault(w => w.Text.Equals("Amount", StringComparison.OrdinalIgnoreCase) && w.BoundingBox.Left > debit.BoundingBox.Left && w.BoundingBox.Left < credit.BoundingBox.Left);
        var creditAmount = line.FirstOrDefault(w => w.Text.Equals("Amount", StringComparison.OrdinalIgnoreCase) && w.BoundingBox.Left > credit.BoundingBox.Left && (balance == null || w.BoundingBox.Left < balance.BoundingBox.Left));
        var cheque = Find("Charge") != null && debitAmount != null && creditAmount != null && balance != null;
        return new(date.BoundingBox.Left, description.BoundingBox.Left, debit.BoundingBox.Left, credit.BoundingBox.Left,
            balance?.BoundingBox.Left ?? credit.BoundingBox.Left + credit.BoundingBox.Left - debit.BoundingBox.Left,
            cheque ? debitAmount!.BoundingBox.Right : null, cheque ? creditAmount!.BoundingBox.Right : null, cheque ? balance!.BoundingBox.Right : null);
    }

    private static IEnumerable<List<Word>> Lines(List<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            if (lines.Count == 0 || Math.Abs(lines[^1][0].BoundingBox.Bottom - word.BoundingBox.Bottom) > 3) lines.Add([word]);
            else lines[^1].Add(word);
        }
        return lines.Select(line => line.OrderBy(w => w.BoundingBox.Left).ToList());
    }

    // Cheque amounts align at the right edge of "Amount" / "Balance". This preserves
    // thousands groups even when their left edge extends past the column heading.
    private record AmountCell(string Text, List<Word> Words);
    private static AmountCell MoneyCell(List<Word> line, double right)
    {
        var index = line.FindLastIndex(w => Math.Abs(w.BoundingBox.Right - right) <= 3);
        if (index < 0) return new("", []);
        var first = index;
        while (first > 0 && line[first].BoundingBox.Left - line[first - 1].BoundingBox.Right is >= 0 and <= 6
            && Regex.IsMatch(line[first - 1].Text, @"^[+-]?\d{1,3}$")) first--;
        var words = line.Skip(first).Take(index - first + 1).ToList();
        return new(string.Join(" ", words.Select(w => w.Text)), words);
    }

    private static bool TryDate(string text, out DateTime date) => DateTime.TryParseExact(text, Dates, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    private static bool TryMoney(string text, out decimal value, bool signed = false)
    {
        value = 0;
        var clean = text.Trim().Replace('\u00a0', ' ').Replace('\u202f', ' ');
        if (signed && clean.EndsWith('-')) clean = "-" + clean[..^1];
        if (!Regex.IsMatch(clean, @"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+|\d{1,3}(?: \d{3})+)(?:\.\d{2})$")) return false;
        return decimal.TryParse(clean.Replace(" ", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out value) && FinanceRules.ValidMoney(value) && (signed || value >= 0);
    }
    private static string Join(string? first, string next) => first == null ? next : first + " " + next;
}
