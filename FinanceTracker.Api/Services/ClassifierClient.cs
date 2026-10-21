namespace FinanceTracker.Api.Services;
public interface IClassifierClient
{
    Task<string?> Suggest(string description, CancellationToken cancellationToken = default);
}
public class ClassifierClient(HttpClient client) : IClassifierClient
{
    public async Task<string?> Suggest(string description, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("predict", new { description }, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var prediction = await response.Content.ReadFromJsonAsync<Prediction>(cancellationToken);
            return string.IsNullOrWhiteSpace(prediction?.Category) ? null : FinanceRules.Category(prediction.Category);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or ArgumentException) { return null; }
    }
    private record Prediction(string Category);
}
