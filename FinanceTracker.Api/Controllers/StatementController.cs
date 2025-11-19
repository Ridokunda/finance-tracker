using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq;

namespace FinanceTracker.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatementController : ControllerBase
    {
        private static readonly char[] PossibleDelimiters = { ',', ';', '\t', '|' };
        private static readonly string[] SupportedDateFormats =
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            "dd-MM-yyyy",
            "MM-dd-yyyy"
        };

        private readonly ApplicationDbContext _db;
        private readonly TransactionCategorizer _categorizer;

        public StatementController(ApplicationDbContext db, TransactionCategorizer categorizer)
        {
            _db = db;
            _categorizer = categorizer;
        }

        [Authorize]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadStatement(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Only CSV files are supported for now.");
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var contents = await reader.ReadToEndAsync();

            var rows = contents
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            if (rows.Length <= 1)
            {
                return BadRequest("The uploaded file does not contain any rows to import.");
            }

            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);
            var transactions = new List<Transaction>();

            foreach (var (rawRow, index) in rows.Skip(1).Select((row, idx) => (row, idx + 2)))
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
                    Category = _categorizer.Predict(description, amount)
                };

                transactions.Add(transaction);
            }

            if (transactions.Count == 0)
            {
                return BadRequest("No transactions could be parsed from the uploaded statement.");
            }

            _db.Transactions.AddRange(transactions);
            await _db.SaveChangesAsync();

            _categorizer.Train();

            return Ok(new { message = "Statement uploaded", count = transactions.Count });
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
    }
}
