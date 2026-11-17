using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using FinanceTracker.Api.Models;
using UglyToad.PdfPig;

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

    public ParsedStatement Pdf(Stream stream)
    {
        try
        {
            using var document = PdfDocument.Open(stream);
            if (document.IsEncrypted) throw new ArgumentException("Password-protected PDFs are not supported.");
            if (document.NumberOfPages > 100) throw new ArgumentException("PDFs are limited to 100 pages.");
            var pages = document.GetPages().Select(p => p.GetWords().ToList()).ToList();
            if (pages.Any(p => p.Count == 0)) throw new ArgumentException("Scanned or unreadable PDF: text-based statements are required. OCR is not supported.");
            return AbsaPdfParser.Parse(pages);
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new ArgumentException("Cannot read this PDF. Password-protected, damaged and unsupported PDFs are not accepted."); }
    }
    private static string Join(string? first, string next) => first == null ? next : first + " " + next;
}
