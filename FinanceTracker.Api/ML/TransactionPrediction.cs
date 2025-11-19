using Microsoft.ML.Data;

public class TransactionPrediction
{
    [ColumnName("PredictedLabel")]
    public string Category { get; set; }
}
