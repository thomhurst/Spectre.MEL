using Microsoft.Extensions.Logging;

namespace MEL.Spectre.Provider;

internal static class SpectreConsoleLoggerFilter
{
    public static bool IsEnabled(LoggerFilterOptions options, string categoryName, LogLevel logLevel)
    {
        SelectRule(options, categoryName, out var minLevel, out var filter);
        return (minLevel is null || logLevel >= minLevel)
            && (filter is null || filter(typeof(SpectreConsoleLoggerProvider).FullName, categoryName, logLevel));
    }

    // Microsoft.Extensions.Logging keeps LoggerRuleSelector internal. Mirror its selection rules so this query
    // stays consistent with ILoggerFactory for both the provider's full name and its public alias.
    private static void SelectRule(
        LoggerFilterOptions options,
        string categoryName,
        out LogLevel? minLevel,
        out Func<string?, string?, LogLevel, bool>? filter)
    {
        LoggerFilterRule? selectedRule = null;
        var providerName = typeof(SpectreConsoleLoggerProvider).FullName;

        foreach (var rule in options.Rules)
        {
            if (IsBetter(rule, selectedRule, providerName, categoryName)
                || IsBetter(rule, selectedRule, SpectreConsoleLoggerProvider.Alias, categoryName))
            {
                selectedRule = rule;
            }
        }

        if (selectedRule is null)
        {
            minLevel = options.MinLevel;
            filter = null;
        }
        else
        {
            minLevel = selectedRule.LogLevel;
            filter = selectedRule.Filter;
        }
    }

    private static bool IsBetter(
        LoggerFilterRule rule,
        LoggerFilterRule? current,
        string? providerName,
        string categoryName)
    {
        if (rule.ProviderName is not null && rule.ProviderName != providerName)
        {
            return false;
        }

        if (rule.CategoryName is { } ruleCategory && !MatchesCategory(ruleCategory, categoryName))
        {
            return false;
        }

        if (current?.ProviderName is not null && rule.ProviderName is null)
        {
            return false;
        }

        if (current?.ProviderName is null && rule.ProviderName is not null)
        {
            return true;
        }

        if (current?.CategoryName is not null)
        {
            if (rule.CategoryName is null || current.CategoryName.Length > rule.CategoryName.Length)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesCategory(string ruleCategory, string categoryName)
    {
        const char Wildcard = '*';
        var wildcardIndex = ruleCategory.IndexOf(Wildcard);
        if (wildcardIndex >= 0 && ruleCategory.IndexOf(Wildcard, wildcardIndex + 1) >= 0)
        {
            throw new InvalidOperationException("Only one wildcard character is allowed in a category filter.");
        }

        var prefix = wildcardIndex < 0 ? ruleCategory.AsSpan() : ruleCategory.AsSpan(0, wildcardIndex);
        var suffix = wildcardIndex < 0 ? default : ruleCategory.AsSpan(wildcardIndex + 1);
        return categoryName.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && categoryName.AsSpan().EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
