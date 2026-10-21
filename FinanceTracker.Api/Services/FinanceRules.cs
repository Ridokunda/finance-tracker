using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceTracker.Api.Services;

public static class FinanceRules
{
    public static DateTime Today => DateTime.UtcNow.AddHours(2).Date;
    public static DateTime Month(int? year = null, int? month = null)
    {
        var today = Today;
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        if (y is < 1900 or > 9998 || m is < 1 or > 12)
            throw new ArgumentException("Choose a valid year (1900–9998) and month (1–12).");
        return new DateTime(y, m, 1);
    }

    public static readonly string[] Categories = ["Income", "Groceries", "Transport", "Shopping", "Utilities", "Rent", "Food", "Entertainment", "Healthcare", "Education", "Insurance", "Fees", "Uncategorized"];
    public static string Category(string? value, IEnumerable<string>? existing = null)
    {
        var clean = Regex.Replace(value?.Trim() ?? "", @"\s+", " ");
        if (clean.Length == 0) return "Uncategorized";
        if (clean.Length > 100) throw new ArgumentException("Categories must be at most 100 characters.");
        return Categories.Concat(existing ?? []).FirstOrDefault(c => c.Equals(clean, StringComparison.OrdinalIgnoreCase)) ?? clean;
    }
    public static string Fingerprint(DateTime date, string description, decimal amount) =>
        $"{date:yyyy-MM-dd}|{Regex.Replace(description.Trim(), @"\s+", " ").ToUpperInvariant()}|{amount.ToString("F2", CultureInfo.InvariantCulture)}";
    public static bool ValidMoney(decimal value) => Math.Abs(value) <= 9999999999999999.99m && decimal.Round(value, 2) == value;
}
