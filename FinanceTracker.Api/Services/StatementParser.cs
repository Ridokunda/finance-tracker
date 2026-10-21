using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using FinanceTracker.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FinanceTracker.Api.Services;

public record CsvMapping(string DateColumn = "Date", string DescriptionColumn = "Description", string AmountColumn = "Amount", string DateFormat = "yyyy-MM-dd", string Delimiter = "");
public record ParsedStatement(List<ImportRow> Rows, List<string> Notices, string[] Columns, DateTime? Start = null, DateTime? End = null);

public class StatementParser
{
    public const int MaxRows = 5000;
    public static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MM-yyyy", "MM-dd-yyyy", "yyyy/MM/dd"];
    public ParsedStatement Csv(Stream stream, CsvMapping mapping)
    {
        if (!DateFormats.Contains(mapping.DateFormat)) throw new ArgumentException("Select an explicit supported date format.");
        if (mapping.Delimiter != "" && !new[] { ",", ";", "\t", "|" }.Contains(mapping.Delimiter)) throw new ArgumentException("Unsupported delimiter.");
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) {
            DetectDelimiter = mapping.Delimiter == "", Delimiter = mapping.Delimiter == "" ? "," : mapping.Delimiter,
            DetectDelimiterValues = [",", ";", "\t", "|"], TrimOptions = TrimOptions.Trim,
            ExceptionMessagesContainRawData = false, MaxFieldSize = 10000
        };
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);
        var rows = new List<ImportRow>();
        try
        {
            if (!csv.Read()) throw new ArgumentException("The CSV is empty.");
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? [];
            var indexes = new[] { mapping.DateColumn, mapping.DescriptionColumn, mapping.AmountColumn }
                .Select(name => Array.FindIndex(headers, h => h.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (indexes.Any(i => i < 0) || indexes.Distinct().Count() != 3)
                return new(rows, ["Select distinct date, description and signed amount columns, then preview again."], headers);
            while (csv.Read())
            {
                if (rows.Count >= MaxRows) throw new ArgumentException($"Statements are limited to {MaxRows} rows.");
                var row = new ImportRow { RowNumber = csv.Parser.Row };
                try
                {
                    row.Description = csv.GetField(indexes[1])?.Trim() ?? "";
                    if (!DateTime.TryParseExact(csv.GetField(indexes[0]), mapping.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        row.Error = "Date does not match the selected format; ambiguous dates require an explicit day/month order.";
                    else row.Date = date.Date;
                    if (!TryAmount(csv.GetField(indexes[2]), out var amount)) row.Error = Join(row.Error, "Invalid signed amount (use decimal point and at most two decimal places).");
                    else row.Amount = amount;
                    if (string.IsNullOrWhiteSpace(row.Description) || row.Description.Length > 2000) row.Error = Join(row.Error, "Description is required and must be at most 2000 characters.");
                    if (csv.Parser.Count != headers.Length) row.Error = Join(row.Error, "Field count differs from the header.");
                }
                catch (CsvHelperException) { row.Error = "Malformed CSV row. Correct the source file and preview again."; }
                row.Included = row.Error == null;
                rows.Add(row);
            }
            return new(rows, [], headers);
        }
        catch (CsvHelperException) { throw new ArgumentException("Malformed CSV quoting or fields. Correct the file and preview again."); }
    }

    public static bool TryAmount(string? text, out decimal amount)
    {
        amount = 0;
        var clean = (text ?? "").Trim();
        // Strict grouping prevents malformed values such as 1,2 from becoming 12.
        if (!Regex.IsMatch(clean, @"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d{1,2})?$")) return false;
        return decimal.TryParse(clean, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out amount) && amount != 0 && FinanceRules.ValidMoney(amount);
    }

    // Conservative positional adapter. Real ABSA fixture acceptance is pending; no OCR or guessed signs.
    public ParsedStatement Pdf(Stream stream)
    {
        try
        {
            using var document = PdfDocument.Open(stream);
            if (document.IsEncrypted) throw new ArgumentException("Password-protected PDFs are not supported.");
            if (document.NumberOfPages > 100) throw new ArgumentException("PDFs are limited to 100 pages.");
            var pages = document.GetPages().Select(p => p.GetWords().ToList()).ToList();
            if (pages.Any(p => p.Count == 0)) throw new ArgumentException("Scanned or unreadable PDF: text-based statements are required. OCR is not supported.");
            if (!pages.SelectMany(p => p).Any(w => w.Text.Equals("ABSA", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Unsupported PDF. Only the ABSA date/description/debit/credit table layout is accepted.");
            var rows = new List<ImportRow>();
            var notices = new List<string> { "ABSA PDF support is provisional until verified against supplied sanitized statements. Check every row and total." };
            DateTime? start = null, end = null;
            decimal? debitTotal = null, creditTotal = null;
            ImportRow? previous = null;
            var lineNumber = 0;
            foreach (var words in pages)
            {
                double? dateX = null, descX = null, debitX = null, creditX = null, balanceX = null;
                // Cluster by baseline within 3 points rather than PDF content-stream order.
                var lines = new List<List<Word>>();
                foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
                {
                    var line = lines.LastOrDefault();
                    if (line == null || Math.Abs(line[0].BoundingBox.Bottom - word.BoundingBox.Bottom) > 3) lines.Add([word]);
                    else line.Add(word);
                }
                foreach (var unordered in lines)
                {
                    lineNumber++;
                    var line = unordered.OrderBy(w => w.BoundingBox.Left).ToList();
                    var text = string.Join(" ", line.Select(w => w.Text));
                    if (Regex.IsMatch(text, @"statement.*(?:period|from|to)", RegexOptions.IgnoreCase))
                    {
                        var dates = Regex.Matches(text, @"\d{2}/\d{2}/\d{4}").Select(m => DateTime.TryParseExact(m.Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? (DateTime?)d : null).Where(d => d != null).ToList();
                        if (dates.Count >= 2) { start = dates[0]; end = dates[1]; }
                    }
                    Word? Header(string value) => line.FirstOrDefault(w => w.Text.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (Header("Date") != null && Header("Description") != null && Header("Debit") != null && Header("Credit") != null)
                    {
                        dateX = Header("Date")!.BoundingBox.Left;
                        descX = Header("Description")!.BoundingBox.Left;
                        debitX = Header("Debit")!.BoundingBox.Left;
                        creditX = Header("Credit")!.BoundingBox.Left;
                        balanceX = Header("Balance")?.BoundingBox.Left ?? creditX + (creditX - debitX);
                        continue;
                    }
                    var totalMatch = Regex.Match(text, @"^Total\s+(?<kind>debits|credits)\s+(?<amount>[\d,]+\.\d{2})$", RegexOptions.IgnoreCase);
                    if (totalMatch.Success && decimal.TryParse(totalMatch.Groups["amount"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var total))
                    {
                        if (totalMatch.Groups["kind"].Value.Equals("debits", StringComparison.OrdinalIgnoreCase)) debitTotal = total; else creditTotal = total;
                        previous = null;
                        continue;
                    }
                    if (descX == null) continue;
                    // Assign words to the recognized column intervals with an eight-point tolerance.
                    string Cell(double left, double right) => string.Join(" ", line.Where(w => w.BoundingBox.Left >= left - 8 && w.BoundingBox.Left < right - 8).Select(w => w.Text));
                    var dateText = Cell(dateX!.Value, descX.Value);
                    var description = Cell(descX.Value, debitX!.Value);
                    var debitText = Cell(debitX.Value, creditX!.Value);
                    var creditText = Cell(creditX.Value, balanceX!.Value);
                    if (Regex.IsMatch(text, @"^(?:Page\b|Opening balance\b|Closing balance\b|Balance brought\b|Balance carried\b)", RegexOptions.IgnoreCase)) { previous = null; continue; }
                    if (dateText == "" && debitText == "" && creditText == "" && description != "" && previous != null)
                    { previous.Description += " " + description; continue; }
                    if (dateText == "" && description == "" && debitText == "" && creditText == "") continue;
                    var row = new ImportRow { RowNumber = lineNumber, Description = description };
                    if (DateTime.TryParseExact(dateText, new[] { "dd/MM/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) row.Date = date.Date;
                    else row.Error = "Unreadable transaction date; expected dd/MM/yyyy or yyyy-MM-dd.";
                    if (TryAmount(debitText, out var debit) && debit > 0 && creditText == "") row.Amount = -debit;
                    else if (TryAmount(creditText, out var credit) && credit > 0 && debitText == "") row.Amount = credit;
                    else row.Error = Join(row.Error, "Could not determine a single debit or credit amount.");
                    if (description == "") row.Error = Join(row.Error, "Missing description.");
                    row.Included = row.Error == null;
                    rows.Add(row);
                    previous = row;
                    if (rows.Count > MaxRows) throw new ArgumentException($"Statements are limited to {MaxRows} rows.");
                }
            }
            if (rows.Count == 0) throw new ArgumentException("Unsupported ABSA PDF layout: no date/description/debit/credit table could be read. Use a generic CSV export with explicit columns.");
            if (start != null && end != null)
                foreach (var row in rows.Where(r => r.Date < start || r.Date > end)) { row.Error = Join(row.Error, "Date falls outside the statement period."); row.Included = false; }
            if (debitTotal != null && -rows.Where(r => r.Amount < 0).Sum(r => r.Amount ?? 0) != debitTotal) notices.Add("Reconciliation mismatch: parsed expenses do not match statement total debits.");
            if (creditTotal != null && rows.Where(r => r.Amount > 0).Sum(r => r.Amount ?? 0) != creditTotal) notices.Add("Reconciliation mismatch: parsed income does not match statement total credits.");
            if (debitTotal == null && creditTotal == null) notices.Add("No recognized statement totals were available for reconciliation.");
            return new(rows, notices, [], start, end);
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new ArgumentException("Cannot read this PDF. Password-protected, damaged and unsupported PDFs are not accepted."); }
    }
    private static string Join(string? first, string next) => first == null ? next : first + " " + next;
}
