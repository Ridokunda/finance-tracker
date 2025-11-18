using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using FinanceTracker.Api.Data;
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
    }
}
