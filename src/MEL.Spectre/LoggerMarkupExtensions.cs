using Microsoft.Extensions.Logging;
using Spectre.Console;
using MEL.Spectre.Provider;

namespace MEL.Spectre;

public static class LoggerMarkupExtensions
{
    /// <summary>
    /// Logs trusted Spectre markup at <see cref="LogLevel.Information"/> for this event only.
    /// Escape untrusted values with <see cref="Markup.Escape(string)"/> before including them.
    /// </summary>
    public static void LogMarkup(this ILogger logger, string markup) =>
        LogMarkup(logger, LogLevel.Information, default, null, markup);

    /// <summary>
    /// Logs trusted Spectre markup at <paramref name="logLevel"/> for this event only.
    /// Escape untrusted values with <see cref="Markup.Escape(string)"/> before including them.
    /// </summary>
    public static void LogMarkup(this ILogger logger, LogLevel logLevel, string markup) =>
        LogMarkup(logger, logLevel, default, null, markup);

    /// <summary>
    /// Logs trusted Spectre markup for this event only.
    /// Escape untrusted values with <see cref="Markup.Escape(string)"/> before including them.
    /// </summary>
    public static void LogMarkup(this ILogger logger, LogLevel logLevel, EventId eventId, Exception? exception, string markup)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(markup);

        var state = new MarkupLogState(markup);
        logger.Log(logLevel, eventId, state, exception, static (value, _) => value.ToString());
    }

    private sealed class MarkupLogState : IMarkupLogState
    {
        public MarkupLogState(string markup)
        {
            Markup = markup;
        }

        public string Markup { get; }

        public override string ToString() => Markup;
    }
}
