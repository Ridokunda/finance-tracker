using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Net.Http.Json;
using FinanceTracker.Api.Services;

namespace FinanceTracker.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IClassifierClient _classifier;
        public TransactionController(ApplicationDbContext db, IClassifierClient classifier)
        {
            _db = db;
            _classifier = classifier;
        }
        [Authorize]
        [HttpGet]
        public IActionResult Get([FromQuery] string? type, [FromQuery] string? category, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] string? search = null)
        {
            var userIdValue = User.FindFirstValue("UserId");
            if (!int.TryParse(userIdValue, out var userId))
            {
                return Unauthorized("Invalid user context.");
            }

            var query = _db.Transactions.Where(t => t.UserId == userId);
            if (from > to) return BadRequest("Start date must be on or before end date.");
            if (from != null) query = query.Where(t => t.Date >= from.Value.Date);
            if (to != null) query = query.Where(t => t.Date <= to.Value.Date);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(t => t.Description.Contains(search.Trim()));

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
                query = query.Where(t => t.Category != null && t.Category.ToLower() == categoryLower);
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
        [HttpPost("classify")]
        public async Task<IActionResult> Classify([FromBody] ClassifyTransactionRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest("Description is required.");
            }

            var category = await _classifier.Suggest(request.Description.Trim(), HttpContext.RequestAborted);
            return category == null ? StatusCode(503, new { message = "The classification service is unavailable. Choose a category manually." }) : Ok(new { category });
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

            if (request.Amount == 0 || !FinanceRules.ValidMoney(request.Amount) || request.Description.Length > 2000 || request.Category?.Length > 100)
            {
                return BadRequest("Amount must be non-zero.");
            }

            var entity = new Transaction
            {
                UserId = userId,
                Date = request.Date == default ? FinanceRules.Today : request.Date.Date,
                Description = request.Description.Trim(),
                Amount = request.Amount,
                Category = FinanceRules.Category(request.Category, _db.Transactions.Where(t => t.UserId == userId).Select(t => t.Category).Distinct().ToList())
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

        [Authorize]
        [HttpPut("{id:int}")]
        public IActionResult Update(int id, [FromBody] UpdateTransactionRequest? request)
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

            if (request.Amount == 0 || !FinanceRules.ValidMoney(request.Amount) || request.Description.Length > 2000 || request.Category?.Length > 100)
            {
                return BadRequest("Amount must be non-zero.");
            }

            var entity = _db.Transactions.FirstOrDefault(t => t.Id == id && t.UserId == userId);
            if (entity == null)
            {
                return NotFound("Transaction not found.");
            }

            entity.Date = request.Date == default ? entity.Date : request.Date.Date;
            entity.Description = request.Description.Trim();
            entity.Amount = request.Amount;
            entity.Category = FinanceRules.Category(request.Category, _db.Transactions.Where(t => t.UserId == userId).Select(t => t.Category).Distinct().ToList());

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

        public class UpdateTransactionRequest
        {
            public DateTime Date { get; set; }
            public string Description { get; set; } = string.Empty;
            public string? Category { get; set; }
            public decimal Amount { get; set; }
        }

        public class ClassifyTransactionRequest
        {
            public string Description { get; set; } = string.Empty;
        }

        private sealed class PredictionResponse
        {
            public string? Category { get; set; }
        }

        [Authorize]
        [HttpGet("net-cash-flow")]
        public IActionResult GetBalance()
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);

            var income = _db.Transactions.Where(t => t.UserId == userId && t.Amount > 0).Sum(t => t.Amount);
            var expenses = _db.Transactions.Where(t => t.UserId == userId && t.Amount < 0).Sum(t => t.Amount);

            var balance = income + expenses;

            return Ok(new { netCashFlow = balance });
        }

        [Authorize, HttpGet("categories")]
        public IActionResult Categories()
        {
            var userId = int.Parse(User.FindFirst("UserId")!.Value);
            var existing = _db.Transactions.Where(t => t.UserId == userId).Select(t => t.Category).Distinct().ToList();
            var budgetNames = _db.MonthlyBudgets.Where(b => b.UserId == userId).SelectMany(b => b.CategoryLimits).Select(c => c.Category).ToList();
            return Ok(FinanceRules.Categories.Concat(existing).Concat(budgetNames).Select(c => FinanceRules.Category(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c));
        }

    }
}
