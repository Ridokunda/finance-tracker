using System.Text.RegularExpressions;
using FinanceTracker.Api.Models;

namespace FinanceTracker.Api.Services;

public static class CategoryRuleMatcher
{
    public static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    // A more specific phrase wins. Equal-length matches use the oldest saved rule.
    public static CategorizationRule? Match(IEnumerable<CategorizationRule> rules, string description)
    {
        var text = Normalize(description);
        return rules.Where(r => r.Enabled && text.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Pattern.Length).ThenBy(r => r.Id).FirstOrDefault();
    }
}
