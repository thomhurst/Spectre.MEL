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
    /// Controls how raw ANSI/VT escape sequences embedded in message content (e.g. relayed stdout from
    /// child processes) are handled. Defaults to <see cref="EmbeddedAnsiMode.Convert"/>: SGR color/style
    /// sequences are translated into Spectre markup so embedded colors render correctly nested inside the
    /// theme's message style, and all other control sequences are removed. Use
    /// <see cref="EmbeddedAnsiMode.Strip"/> to drop every sequence, or
    /// <see cref="EmbeddedAnsiMode.Passthrough"/> to restore the previous raw behaviour.
    /// </summary>
    public EmbeddedAnsiMode EmbeddedAnsi { get; set; } = EmbeddedAnsiMode.Convert;

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
    /// through innocuously-named placeholders (e.g. <c>{Url}</c> containing an embedded token). Defaults cover
    /// well-known GitHub, GitLab, AWS, Slack, JWT, and private-key formats. Clear or extend the mutable list
    /// during configuration as needed. Snapshotted at provider construction.
    /// </summary>
    public List<string> MaskedValuePatterns { get; } =
    [
        @"(?-i:(?<![A-Za-z0-9])gh[pousr]_[A-Za-z0-9]{36}(?![A-Za-z0-9]))",
        @"(?-i:(?<![A-Za-z0-9_])github_pat_[A-Za-z0-9_]{22,}(?![A-Za-z0-9_]))",
        @"(?-i:(?<![A-Za-z0-9_-])glpat-[A-Za-z0-9_-]{20,}(?![A-Za-z0-9_-]))",
        @"(?-i:(?<![0-9A-Z])AKIA[0-9A-Z]{16}(?![0-9A-Z]))",
        @"(?-i:(?<![A-Za-z0-9-])xox[baprs]-[A-Za-z0-9-]{10,}(?![A-Za-z0-9-]))",
        @"(?-i:(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}(?![A-Za-z0-9_-]))",
        @"(?s-i:-----BEGIN (?<pem>[A-Z ]*PRIVATE KEY)-----.*?(?:-----END \k<pem>-----|\z))",
    ];

    /// <summary>
    /// When true, value patterns also scan literal and pre-formatted message text, catching secrets
    /// embedded through string interpolation. Disable to retain placeholder-only value scanning.
    /// Snapshotted at provider construction. Defaults to true.
    /// </summary>
    public bool MaskValuePatternsInMessageText { get; set; } = true;

    public IAnsiConsole? Console { get; set; }

    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan EnqueueWaitTimeout { get; set; } = TimeSpan.FromSeconds(1);
}
