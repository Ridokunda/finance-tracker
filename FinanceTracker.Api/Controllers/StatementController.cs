using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace FinanceTracker.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatementController : ControllerBase
    {
        private static readonly char[] PossibleDelimiters = { ',', ';', '\t', '|' };
        private static readonly string[] SupportedStatementExtensions = { ".csv", ".pdf" };
        private static readonly string[] SupportedDateFormats =
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            "dd-MM-yyyy",
            "MM-dd-yyyy",
            "yyyy-MM-dd",
        };
        private static readonly Regex PdfTransactionLineRegex = new(
            @"(?<date>\d{1,4}[/-]\d{1,2}[/-]\d{1,4})\s+(?<description>.+?)\s+(?<amount>[+-]?\$?\d[\d,]*(?:\.\d{2})?)$",
            RegexOptions.Compiled);
        private static readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8000/") };

        private readonly ApplicationDbContext _db;

        public StatementController(ApplicationDbContext db)
        {
            _db = db;
        }

        [Authorize]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadStatement(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension)
                || !SupportedStatementExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest("Only CSV and PDF files are supported.");
            }

            List<(string RawRow, int RowNumber)> rows;
            if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                rows = await ReadCsvRowsAsync(file);
            }
            else
            {
                rows = ReadPdfRows(file);
            }

            if (rows.Count == 0)
            {
                return BadRequest("The uploaded file does not contain any rows to import.");
            }

            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);
            var transactions = new List<Transaction>();

            foreach (var (rawRow, index) in rows)
            {
                if (string.IsNullOrWhiteSpace(rawRow))
                {
                    continue;
                }

                if (!TryParseTransactionRow(rawRow, out var date, out var description, out var amount, out var parseError))
                {
                    return BadRequest($"Row {index}: {parseError}");
                }

                var transaction = new Transaction
                {
                    UserId = userId,
                    Date = date,
                    Description = description,
                    Amount = amount,
                    Category = "Uncategorized"
                };

                try
                {
                    var payload = new { description = transaction.Description };
                    var response = await _httpClient.PostAsJsonAsync("predict", payload);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<PredictionResponse>();
                        if (result != null && !string.IsNullOrEmpty(result.Category))
                        {
                            transaction.Category = result.Category;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception, but continue processing the statement
                    Console.WriteLine($"ML Service unavailable: {ex.Message}");
                }

                transactions.Add(transaction);
            }

            if (transactions.Count == 0)
            {
                return BadRequest("No transactions could be parsed from the uploaded statement.");
            }

            _db.Transactions.AddRange(transactions);
            await _db.SaveChangesAsync();

            // Update monthly budgets for each transaction's month
            await UpdateMonthlyBudgets(userId, transactions);

            return Ok(new { message = "Statement uploaded", count = transactions.Count });
        }

        private static async Task<List<(string RawRow, int RowNumber)>> ReadCsvRowsAsync(IFormFile file)
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var contents = await reader.ReadToEndAsync();

            var rows = contents
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            if (rows.Length <= 1)
            {
                return new List<(string RawRow, int RowNumber)>();
            }

            return rows
                .Skip(1)
                .Select((row, idx) => (RawRow: row, RowNumber: idx + 2))
                .ToList();
        }

        private static List<(string RawRow, int RowNumber)> ReadPdfRows(IFormFile file)
        {
            using var stream = file.OpenReadStream();
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);

            var extractedText = ExtractTextFromPdfBytes(memoryStream.ToArray());

            var rows = new List<(string RawRow, int RowNumber)>();
            var rowNumber = 1;

            var lines = extractedText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => Regex.Replace(line, @"\s+", " ").Trim());

            foreach (var line in lines)
            {
                if (TryConvertPdfLineToCsvRow(line, out var csvRow))
                {
                    rows.Add((csvRow, rowNumber));
                }

                rowNumber++;
            }

            return rows;
        }

        private static string ExtractTextFromPdfBytes(byte[] bytes)
        {
            var raw = Encoding.Latin1.GetString(bytes);
            var matches = Regex.Matches(raw, @"\((?<text>(?:\\.|[^\\)])+)\)");
            var lines = new List<string>();

            foreach (Match match in matches)
            {
                var decoded = DecodePdfString(match.Groups["text"].Value);
                if (string.IsNullOrWhiteSpace(decoded))
                {
                    continue;
                }

                lines.Add(decoded);
            }

            if (lines.Count > 0)
            {
                return string.Join('\n', lines);
            }

            // Fallback for PDFs where text isn't encoded as simple literal strings.
            var printable = Regex.Matches(raw, @"[A-Za-z0-9/\-\.,\$:\s]{25,}")
                .Select(match => match.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return string.Join('\n', printable);
        }

        private static string DecodePdfString(string value)
        {
            return value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\(", "(")
                .Replace("\\)", ")")
                .Replace("\\\\", "\\");
        }

        private static bool TryConvertPdfLineToCsvRow(string line, out string csvRow)
        {
            csvRow = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            if (DetectDelimiter(line) != null)
            {
                csvRow = line;
                return true;
            }

            var match = PdfTransactionLineRegex.Match(line);
            if (!match.Success)
            {
                return false;
            }

            var date = match.Groups["date"].Value.Trim();
            var description = match.Groups["description"].Value.Trim();
            var amount = match.Groups["amount"].Value.Trim();

            if (description.Length == 0)
            {
                return false;
            }

            csvRow = $"{date},\"{description.Replace("\"", "\"\"")}\",{amount}";
            return true;
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetTransactions()
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);

            var transactions = await _db.Transactions
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            return Ok(transactions);
        }

        private static bool TryParseTransactionRow(
            string rawLine,
            out DateTime date,
            out string description,
            out decimal amount,
            out string? error)
        {
            date = default;
            description = string.Empty;
            amount = default;

            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                error = "Empty row.";
                return false;
            }

            var delimiter = DetectDelimiter(line);
            if (delimiter == null)
            {
                error = "Unable to detect a column separator.";
                return false;
            }

            var parts = line
                .Split(delimiter.Value)
                .Select(part => part.Trim().Trim('"'))
                .ToArray();

            if (parts.Length < 3)
            {
                error = "Expected at least three columns (date, description, amount).";
                return false;
            }

            var dateText = parts[0];
            var descriptionText = parts[1];
            var amountText = parts[2];

            if (!TryParseDate(dateText, out date))
            {
                error = $"Invalid date '{dateText}'.";
                return false;
            }

            if (!TryParseDecimal(amountText, out amount))
            {
                error = $"Invalid amount '{amountText}'.";
                return false;
            }

            description = descriptionText;
            error = null;
            return true;
        }

        private static char? DetectDelimiter(string line)
        {
            foreach (var delimiter in PossibleDelimiters)
            {
                if (line.Contains(delimiter))
                {
                    return delimiter;
                }
            }

            return null;
        }

        private static bool TryParseDate(string input, out DateTime date)
        {
            return DateTime.TryParseExact(
                       input,
                       SupportedDateFormats,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                       out date)
                   || DateTime.TryParse(
                       input,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                       out date)
                   || DateTime.TryParse(
                       input,
                       CultureInfo.CurrentCulture,
                       DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                       out date);
        }

        private static bool TryParseDecimal(string input, out decimal amount)
        {
            const NumberStyles styles = NumberStyles.Number |
                                        NumberStyles.AllowCurrencySymbol |
                                        NumberStyles.AllowThousands |
                                        NumberStyles.AllowLeadingSign;

            return decimal.TryParse(input, styles, CultureInfo.InvariantCulture, out amount)
                   || decimal.TryParse(input, styles, CultureInfo.CurrentCulture, out amount);
        }

        private async Task UpdateMonthlyBudgets(int userId, List<Transaction> transactions)
        {
            try
            {
                // Group transactions by year and month
                var transactionsByMonth = transactions
                    .GroupBy(t => new { t.Date.Year, t.Date.Month })
                    .ToList();

                Console.WriteLine($"[Budget Update] Processing {transactionsByMonth.Count} months for user {userId}");

                foreach (var monthGroup in transactionsByMonth)
                {
                    var year = monthGroup.Key.Year;
                    var month = monthGroup.Key.Month;

                    // Calculate expenses from newly uploaded transactions
                    var uploadedExpenses = monthGroup
                        .Where(t => t.Amount < 0)
                        .Sum(t => Math.Abs(t.Amount));

                    Console.WriteLine($"[Budget Update] Month: {year}-{month:D2}, Uploaded Expenses: {uploadedExpenses}");

                    // Find or create MonthlyBudget for this month
                    var budget = await _db.MonthlyBudgets
                        .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year && b.Month == month);

                    if (budget == null)
                    {
                        // Calculate total expenses from ALL transactions in this month
                        var allMonthlyExpenses = await _db.Transactions
                            .Where(t => t.UserId == userId && t.Date.Year == year && t.Date.Month == month && t.Amount < 0)
                            .SumAsync(t => Math.Abs(t.Amount));

                        Console.WriteLine($"[Budget Update] Creating new budget. Total Monthly Expenses: {allMonthlyExpenses}");

                        // Create new budget entry with calculated expenses
                        budget = new MonthlyBudget
                        {
                            UserId = userId,
                            Year = year,
                            Month = month,
                            LimitAmount = allMonthlyExpenses
                        };
                        _db.MonthlyBudgets.Add(budget);
                    }
                    else
                    {
                        var previousLimit = budget.LimitAmount;
                        budget.LimitAmount -= uploadedExpenses;

                        Console.WriteLine($"[Budget Update] Updating existing budget. Previous: {previousLimit}, Subtracting: {uploadedExpenses}, New: {budget.LimitAmount}");
                    }
                }

                await _db.SaveChangesAsync();
                Console.WriteLine("[Budget Update] Successfully saved budget changes");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Budget Update ERROR] {ex.Message}");
                Console.WriteLine($"[Budget Update ERROR] {ex.StackTrace}");
                throw;
            }
        }

        [Authorize]
        [HttpGet("budget/{year}/{month}")]
        public async Task<IActionResult> GetMonthlyBudget(int year, int month)
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);

            var budget = await _db.MonthlyBudgets
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year && b.Month == month);

            if (budget == null)
            {
                return NotFound(new { message = $"No budget found for {year}-{month:D2}" });
            }

            // Calculate actual expenses for this month
            var totalExpenses = await _db.Transactions
                .Where(t => t.UserId == userId && t.Date.Year == year && t.Date.Month == month && t.Amount < 0)
                .SumAsync(t => Math.Abs(t.Amount));

            return Ok(new
            {
                budget.Id,
                budget.Year,
                budget.Month,
                budget.LimitAmount,
                TotalExpenses = totalExpenses,
                Remaining = budget.LimitAmount - totalExpenses
            });
        }

        public class PredictionResponse
        {
            public string Category { get; set; }
        }
    }
}
