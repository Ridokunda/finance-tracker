using Microsoft.ML;
using FinanceTracker.Api.Data;

public class TransactionCategorizer
{
    private readonly ApplicationDbContext _db;
    private readonly string _modelPath = "ML/transactionModel.zip";
    private readonly MLContext _ml;

    private ITransformer _model;
    private PredictionEngine<TransactionInput, TransactionPrediction> _predictor;

    public TransactionCategorizer(ApplicationDbContext db)
    {
        _db = db;
        _ml = new MLContext();

        if (File.Exists(_modelPath))
        {
            _model = _ml.Model.Load(_modelPath, out _);
            _predictor = _ml.Model.CreatePredictionEngine<TransactionInput, TransactionPrediction>(_model);
        }
    }

    public string Predict(string description, decimal amount)
    {
        if (_predictor == null)
            return "Uncategorized";

        var input = new TransactionInput
        {
            Description = description,
            Amount = (float)amount
        };

        var prediction = _predictor.Predict(input);
        return prediction.Category ?? "Uncategorized";
    }

    public void Train()
    {
        var data = _db.Transactions
            .Where(t => !string.IsNullOrEmpty(t.Category))
            .Select(t => new TransactionInput
            {
                Description = t.Description,
                Amount = (float)t.Amount,
                Category = t.Category
            }).ToList();

        if (!data.Any())
            return;

        var trainingData = _ml.Data.LoadFromEnumerable(data);

        var pipeline = _ml.Transforms.Text.FeaturizeText("DescriptionFeats", nameof(TransactionInput.Description))
            .Append(_ml.Transforms.NormalizeMinMax(nameof(TransactionInput.Amount)))
            .Append(_ml.Transforms.Concatenate("Features", "DescriptionFeats", nameof(TransactionInput.Amount)))
            .Append(_ml.MulticlassClassification.Trainers.OneVersusAll(
                binaryEstimator: _ml.BinaryClassification.Trainers.FastTree()))
            .Append(_ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        _model = pipeline.Fit(trainingData);

        _ml.Model.Save(_model, trainingData.Schema, _modelPath);

        _predictor = _ml.Model.CreatePredictionEngine<TransactionInput, TransactionPrediction>(_model);
    }
}
