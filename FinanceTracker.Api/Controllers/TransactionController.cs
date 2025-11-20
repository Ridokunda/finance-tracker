using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace FinanceTracker.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public TransactionController(ApplicationDbContext db)
        {
            _db = db;
        }
        [Authorize]
        [HttpGet]
        public IActionResult Get([FromQuery] string? type, [FromQuery] string? category)
        {
            var userIdValue = User.FindFirstValue("UserId");
            if (!int.TryParse(userIdValue, out var userId))
            {
                return Unauthorized("Invalid user context.");
            }

            var query = _db.Transactions.Where(t => t.UserId == userId);

            if (!string.IsNullOrWhiteSpace(type))
            {
                var typeLower = type.ToLowerInvariant();
                if (typeLower == "income")
                {
                    query = query.Where(t => t.Amount > 0);
                }
                else if (typeLower == "expense")
                {
                    query = query.Where(t => t.Amount < 0);
                }
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var categoryLower = category.ToLowerInvariant();
                query = query.Where(t => t.Category != null && t.Category.ToLowerInvariant() == categoryLower);
            }

            var transactions = query
                .OrderByDescending(t => t.Date)
                .Select(t => new
                {
                    t.Id,
                    t.Date,
                    t.Description,
                    t.Amount,
                    t.Category
                })
                .ToList();

            return Ok(transactions);
        }

        [Authorize]
        [HttpPost]
        public IActionResult Create([FromBody] CreateTransactionRequest? request)
        {
            if (request == null)
            {
                return BadRequest("Request body required.");
            }

            var userIdValue = User.FindFirstValue("UserId");
            if (!int.TryParse(userIdValue, out var userId))
            {
                return Unauthorized("Invalid user context.");
            }

            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest("Description is required.");
            }

            if (request.Amount == 0)
            {
                return BadRequest("Amount must be non-zero.");
            }

            var entity = new Transaction
            {
                UserId = userId,
                Date = request.Date == default ? DateTime.UtcNow : request.Date,
                Description = request.Description.Trim(),
                Amount = request.Amount,
                Category = string.IsNullOrWhiteSpace(request.Category) ? "Uncategorized" : request.Category.Trim()
            };

            _db.Transactions.Add(entity);
            _db.SaveChanges();

            return Ok(new
            {
                entity.Id,
                entity.Date,
                entity.Description,
                entity.Amount,
                entity.Category
            });
        }

        public class CreateTransactionRequest
        {
            public DateTime Date { get; set; }
            public string Description { get; set; } = string.Empty;
            public string? Category { get; set; }
            public decimal Amount { get; set; }
        }

        [Authorize]
        [HttpGet("balance")]
        public IActionResult GetBalance()
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);

            var income = _db.Transactions.Where(t => t.UserId == userId && t.Amount > 0).Sum(t => t.Amount);
            var expenses = _db.Transactions.Where(t => t.UserId == userId && t.Amount < 0).Sum(t => t.Amount);

            var balance = income + expenses;

            return Ok(new { balance });
        }

    }
}
