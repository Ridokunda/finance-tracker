namespace FinanceTracker.Api.Models;

public class CategorizationRule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Pattern { get; set; } = "";
    public string Category { get; set; } = "Uncategorized";
    public bool Enabled { get; set; } = true;
}
