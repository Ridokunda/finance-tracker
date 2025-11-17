using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatementController : ControllerBase
    {
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
                return BadRequest("No file uploaded.");

            if (!file.FileName.EndsWith(".csv"))
                return BadRequest("Only CSV files are supported for now.");

            using var reader = new StreamReader(file.OpenReadStream());
            var csv = await reader.ReadToEndAsync();

            var lines = csv.Split('\n');

            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);

            var newTransactions = new List<Transaction>();

            foreach (var line in lines.Skip(1)) // skip header
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');

                var tx = new Transaction
                {
                    UserId = userId,
                    Date = DateTime.Parse(parts[0]),
                    Description = parts[1],
                    Amount = decimal.Parse(parts[2], CultureInfo.InvariantCulture)
                };

                newTransactions.Add(tx);
            }

            _db.Transactions.AddRange(newTransactions);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Statement uploaded", count = newTransactions.Count });
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
    }
}
