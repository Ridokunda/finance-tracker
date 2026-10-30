namespace FinanceTracker.Api.Models;

public class RecurringBill
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string MatchText { get; set; } = "";
    public decimal Amount { get; set; }
    public string Category { get; set; } = "Uncategorized";
    public string Frequency { get; set; } = "monthly";
    public DateTime FirstDueDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Archived { get; set; }
}

public class BillPayment
{
    public int Id { get; set; }
    public int RecurringBillId { get; set; }
    public DateTime DueDate { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
}
