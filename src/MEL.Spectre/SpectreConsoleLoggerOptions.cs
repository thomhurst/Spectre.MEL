using Microsoft.Extensions.Logging;
using Spectre.Console;
using MEL.Spectre.Theme;

namespace MEL.Spectre;

public sealed class SpectreConsoleLoggerOptions
{
    public string Template { get; set; } = "[{Timestamp:HH:mm:ss} {Level:u5} {Category}] {Message}";

    /// <summary>
    /// Entries below this level have their <c>{Level}</c> template segment suppressed, hiding the
    /// noisy <c>[INFO]</c>/<c>INFO </c> prefix on the most common log lines while warnings, errors,
    /// and debug entries still carry their level indicator. When the level is suppressed, any
    /// surrounding bracket pair (<c>[…]</c>) and adjacent inner whitespace in the template are
    /// also stripped so the line doesn't render as an empty <c>[]</c>. Defaults to
    /// <see cref="LogLevel.Trace"/> (no suppression, existing behaviour).
    /// </summary>
    public LogLevel MinimumInlineLevel { get; set; } = LogLevel.Trace;

    public SpectreTheme Theme { get; set; } = SpectreTheme.Default;

    public CiMode CiMode { get; set; } = CiMode.Auto;

    public InteractivityMode InteractivityMode { get; set; } = InteractivityMode.Auto;

    /// <summary>
    /// When true, CI log entries follow the configured console width. Defaults to false so CI log lines are
    /// rendered without wrapping, including when a consumer-supplied console has a narrow profile.
    /// </summary>
    public bool WrapInCi { get; set; }

    public bool IncludeScopes { get; set; } = true;

    public bool IncludeActivity { get; set; } = true;

    /// <summary>
    /// When true, Spectre markup tags (e.g. <c>[green]✓[/]</c>) embedded in message templates are passed through
    /// to the console renderer instead of being escaped. Placeholder values are still escaped. Defaults to false
    /// because most loggers treat the message template as literal text.
    /// </summary>
    public bool AllowMarkupInMessageTemplate { get; set; }

    /// <summary>
    /// When true, entries emitted as native CI annotations use only the rendered <c>{Message}</c> payload.
    /// This avoids duplicate severity and dangling separators from the full output template. Defaults to true;
    /// set to false to retain the complete template, including its inline level.
    /// </summary>
    public bool SuppressInlineLevelOnCiAnnotation { get; set; } = true;

    /// <summary>
    /// Maps log levels to native CI annotations. Critical and error entries emit error annotations,
    /// and warning entries emit warning annotations by default. Debug and trace entries remain ordinary
    /// visible log lines; map them to <see cref="CiAnnotation.Debug"/> to opt into runner debug annotations.
    /// Snapshotted at provider construction.
    /// </summary>
    public Dictionary<LogLevel, CiAnnotation?> CiLevelAnnotations { get; set; } = new()
    {
        [LogLevel.Critical] = CiAnnotation.Error,
        [LogLevel.Error] = CiAnnotation.Error,
        [LogLevel.Warning] = CiAnnotation.Warning,
    };

    public ExceptionFormats ExceptionFormats { get; set; } =
        ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes | ExceptionFormats.ShortenMethods | ExceptionFormats.ShowLinks;

    public int ChannelCapacity { get; set; } = 10_000;

    public BackpressureMode BackpressureMode { get; set; } = BackpressureMode.Wait;

    /// <summary>
    /// Controls whether log entries render on the background consumer or inline before <c>ILogger.Log</c>
    /// returns. Synchronous mode provides strict same-thread ordering for CI hosts that mix logging with
    /// direct console output.
    /// </summary>
    public WriteMode WriteMode { get; set; } = WriteMode.Background;

    public int MaskedValueCacheCapacity { get; set; } = 256;

    /// <summary>
    /// Regex patterns evaluated against placeholder names to decide masking. Snapshotted into a compiled
    /// regex array at provider construction — mutations after the provider starts are ignored.
    /// </summary>
    public List<string> MaskedNamePatterns { get; } =
    [
        "password",
        "pwd",
        "token",
        "secret",
        "apikey|api[_-]?key",
        "bearer",
        "authorization",
        "credential",
    ];

    /// <summary>
    /// Regex patterns evaluated against placeholder string values to decide masking. Catches secrets logged
    /// through innocuously-named placeholders (e.g. <c>{Url}</c> containing an embedded token). Defaults to
    /// empty — opt in by adding patterns. Snapshotted at provider construction.
    /// </summary>
    public List<string> MaskedValuePatterns { get; } = new();

    public IAnsiConsole? Console { get; set; }

    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan EnqueueWaitTimeout { get; set; } = TimeSpan.FromSeconds(1);
}
