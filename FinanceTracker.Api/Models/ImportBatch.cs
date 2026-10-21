namespace FinanceTracker.Api.Models;
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public DateTime? ConfirmedAt { get; set; }
    public int ConfirmedCount { get; set; }
    public string NoticesJson { get; set; } = "[]";
    public DateTime? StatementStart { get; set; }
    public DateTime? StatementEnd { get; set; }
    public List<ImportRow> Rows { get; set; } = [];
    public byte[] Version { get; set; } = [];
}
public class ImportRow
{
    public int Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public DateTime? Date { get; set; }
    public string Description { get; set; } = "";
    public decimal? Amount { get; set; }
    public string Category { get; set; } = "Uncategorized";
    public string? Error { get; set; }
    public bool PossibleDuplicate { get; set; }
    public bool Included { get; set; } = true;
}
