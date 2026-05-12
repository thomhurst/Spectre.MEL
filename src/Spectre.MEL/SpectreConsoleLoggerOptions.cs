using Spectre.Console;
using Spectre.MEL.Theme;

namespace Spectre.MEL;

public sealed class SpectreConsoleLoggerOptions
{
    public string Template { get; set; } = "[{Timestamp:HH:mm:ss} {Level:u4} {Category}] {Message}";

    public SpectreTheme Theme { get; set; } = SpectreTheme.Default;

    public CiMode CiMode { get; set; } = CiMode.Auto;

    public InteractivityMode InteractivityMode { get; set; } = InteractivityMode.Auto;

    public bool IncludeScopes { get; set; } = true;

    public bool IncludeActivity { get; set; } = true;

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
