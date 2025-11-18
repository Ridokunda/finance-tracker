using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using FinanceTracker.Api.Data;
using Microsoft.AspNetCore.Authorization;

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
        public IActionResult Get([FromQuery] string type, [FromQuery] string category)
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);
            var query = _db.Transactions.Where(t => t.UserId == userId);
            if (!string.IsNullOrEmpty(type))
            {
                if (type.ToLower() == "income")
                    query = query.Where(t => t.Amount > 0);
                else if (type.ToLower() == "expense")
                    query = query.Where(t => t.Amount < 0);
            }
            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(t => t.Category.ToLower() == category.ToLower());
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
