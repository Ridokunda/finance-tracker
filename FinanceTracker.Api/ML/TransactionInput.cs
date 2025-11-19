using Microsoft.ML.Data;

public class TransactionInput
{
    [LoadColumn(0)]
    public string Description { get; set; }

    [LoadColumn(1)]
    public float Amount { get; set; }

    [LoadColumn(2)]
    public string Category { get; set; }
}
