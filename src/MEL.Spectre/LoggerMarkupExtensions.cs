using System.Collections;
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

    private sealed class MarkupLogState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly string _markup;

        public MarkupLogState(string markup)
        {
            _markup = markup;
        }

        public int Count => 2;

        public KeyValuePair<string, object?> this[int index] => index switch
        {
            0 => new(StateReader.MarkupEnabledKey, true),
            1 => new(StateReader.OriginalFormatKey, _markup),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _markup;
    }
}
