namespace FinanceTracker.Api.Models;
public class CategoryLimit
{
    public int Id { get; set; }
    public int MonthlyBudgetId { get; set; }
    public string Category { get; set; } = "Uncategorized";
    public decimal LimitAmount { get; set; }
}
