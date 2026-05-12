using Spectre.Console;
using MEL.Spectre.Theme;

namespace MEL.Spectre;

public sealed class SpectreConsoleLoggerOptions
{
    public string Template { get; set; } = "[{Timestamp:HH:mm:ss} {Level:u5} {Category}] {Message}";

    public SpectreTheme Theme { get; set; } = SpectreTheme.Default;

    public CiMode CiMode { get; set; } = CiMode.Auto;

    public InteractivityMode InteractivityMode { get; set; } = InteractivityMode.Auto;

    public bool IncludeScopes { get; set; } = true;

    public bool IncludeActivity { get; set; } = true;

    /// <summary>
    /// When true, Spectre markup tags (e.g. <c>[green]✓[/]</c>) embedded in message templates are passed through
    /// to the console renderer instead of being escaped. Placeholder values are still escaped. Defaults to false
    /// because most loggers treat the message template as literal text.
    /// </summary>
    public bool AllowMarkupInMessageTemplate { get; set; }

    /// <summary>
    /// When true, suppress the rendered level segment (e.g. <c>[WARN]</c>) for entries whose level is also emitted
    /// as a native CI annotation (such as <c>::warning::</c> for GitHub Actions). Avoids duplicate severity
    /// markers on the same line. Defaults to false to preserve existing rendering.
    /// </summary>
    public bool SuppressInlineLevelOnCiAnnotation { get; set; }

    public ExceptionFormats ExceptionFormats { get; set; } =
        ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes | ExceptionFormats.ShortenMethods | ExceptionFormats.ShowLinks;

    public int ChannelCapacity { get; set; } = 10_000;

    public BackpressureMode BackpressureMode { get; set; } = BackpressureMode.Wait;

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

    public IAnsiConsole? Console { get; set; }

    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan EnqueueWaitTimeout { get; set; } = TimeSpan.FromSeconds(1);
}
