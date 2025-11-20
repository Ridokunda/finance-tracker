namespace FinanceTracker.Api.Models
{
    public class MonthlyBudget
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public int Year { get; set; }
        public int Month { get; set; }

        public decimal LimitAmount { get; set; }

        public AppUser User { get; set; }
    }
}
