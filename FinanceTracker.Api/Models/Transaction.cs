namespace FinanceTracker.Api.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        public int UserId { get; set; }   // FK to AppUser

        public DateTime Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public decimal Amount { get; set; }
        

        public AppUser User { get; set; }
    }
}
