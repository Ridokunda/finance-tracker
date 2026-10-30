namespace FinanceTracker.Api.Models;

public class BudgetWarningSettings
{
    public int UserId { get; set; }
    public bool Enabled { get; set; } = true;
    public int ThresholdPercent { get; set; } = 80;
}
