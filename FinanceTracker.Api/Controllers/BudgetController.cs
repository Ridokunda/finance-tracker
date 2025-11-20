using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BudgetController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public BudgetController(ApplicationDbContext db)
        {
            _db = db;
        }

        [Authorize]
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentBudget()
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);
            var now = DateTime.UtcNow;

            var budget = await _db.MonthlyBudgets
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == now.Year && b.Month == now.Month);

            return Ok(budget);
        }

        [Authorize]
        [HttpPost("set")]
        public async Task<IActionResult> SetBudget([FromBody] SetBudgetRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body required.");
            }

            if (request.LimitAmount < 0)
            {
                return BadRequest("Budget must be non-negative.");
            }

            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);
            var now = DateTime.UtcNow;

            var existing = await _db.MonthlyBudgets
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == now.Year && b.Month == now.Month);

            if (existing != null)
            {
                existing.LimitAmount = request.LimitAmount;
            }
            else
            {
                var budget = new MonthlyBudget
                {
                    UserId = userId,
                    Year = now.Year,
                    Month = now.Month,
                    LimitAmount = request.LimitAmount
                };

                _db.MonthlyBudgets.Add(budget);
            }

            await _db.SaveChangesAsync();

            var summary = await BuildBudgetSummaryAsync(userId, now);
            return Ok(summary);
        }

        [Authorize]
        [HttpGet("summary")]
        public async Task<IActionResult> GetBudgetSummary()
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == "UserId").Value);
            var now = DateTime.UtcNow;

            var summary = await BuildBudgetSummaryAsync(userId, now);
            return Ok(summary);
        }

        private async Task<BudgetSummaryResponse> BuildBudgetSummaryAsync(int userId, DateTime reference)
        {
            var start = new DateTime(reference.Year, reference.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);

            var budget = await _db.MonthlyBudgets
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == reference.Year && b.Month == reference.Month);

            var spentRaw = await _db.Transactions
                .Where(t => t.UserId == userId && t.Amount < 0 && t.Date >= start && t.Date < end)
                .SumAsync(t => t.Amount);

            var spent = Math.Abs(spentRaw);
            var budgetAmount = budget?.LimitAmount ?? 0;
            var remaining = budgetAmount - spent;

            return new BudgetSummaryResponse
            {
                Budget = budgetAmount,
                Spent = spent,
                Remaining = remaining,
                Status = remaining < 0 ? "over" : "ok"
            };
        }

        public class SetBudgetRequest
        {
            public decimal LimitAmount { get; set; }
        }

        public class BudgetSummaryResponse
        {
            public decimal Budget { get; set; }
            public decimal Spent { get; set; }
            public decimal Remaining { get; set; }
            public string Status { get; set; } = string.Empty;
        }
    }
}
