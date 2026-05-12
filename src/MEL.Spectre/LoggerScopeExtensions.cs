using Microsoft.Extensions.Logging;
using MEL.Spectre.Scopes;

namespace MEL.Spectre;

public static class LoggerScopeExtensions
{
    /// <summary>
    /// Logs a single, consistently formatted scope-outcome line — intended to be emitted just before
    /// closing a logger scope so the group header and the trailing summary aren't both printed.
    /// The line uses a structured template so renderers can theme each placeholder independently.
    /// </summary>
    public static void LogScopeOutcome(this ILogger logger, ScopeOutcome outcome, string scopeName, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(scopeName);

        var level = outcome switch
        {
            ScopeOutcome.Failure => LogLevel.Error,
            ScopeOutcome.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        var icon = outcome switch
        {
            ScopeOutcome.Success => "✓",
            ScopeOutcome.Failure => "✗",
            ScopeOutcome.Skipped => "○",
            ScopeOutcome.Warning => "⚠",
            _ => "·",
        };

        if (!logger.IsEnabled(level))
        {
            return;
        }

        if (duration.HasValue)
        {
            logger.Log(level, "{Icon} {ScopeName} ({Duration})", icon, scopeName, FormatDuration(duration.Value));
        }
        else
        {
            logger.Log(level, "{Icon} {ScopeName}", icon, scopeName);
        }
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalMinutes >= 1)
        {
            return $"{(int)d.TotalMinutes}m {d.Seconds}s";
        }
        if (d.TotalSeconds >= 1)
        {
            return $"{d.TotalSeconds:0.0}s";
        }
        return $"{(int)d.TotalMilliseconds}ms";
    }
}
