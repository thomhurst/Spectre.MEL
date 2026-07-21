using Spectre.Console;
using MEL.Spectre.Ci;
using MEL.Spectre.Rendering;

namespace MEL.Spectre.Provider;

internal static class AnsiConsoleFactory
{
    public static IAnsiConsole Build(SpectreConsoleLoggerOptions options)
    {
        if (options.Console is not null)
        {
            return options.Console;
        }

        var ciMode = options.CiMode == CiMode.Auto ? CiDetector.DetectFromEnvironment() : options.CiMode;
        var isInteractive = options.InteractivityMode switch
        {
            InteractivityMode.Interactive => true,
            InteractivityMode.NonInteractive => false,
            _ => TtyDetector.IsInteractiveTty(),
        };

        var inCi = ciMode != CiMode.Off;

        var settings = new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(System.Console.Out),
            Interactive = isInteractive ? InteractionSupport.Yes : InteractionSupport.No,
            Ansi = (isInteractive || inCi) ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = ColorSystemSupport.Detect,
        };

        var console = AnsiConsole.Create(settings);
        if (!isInteractive)
        {
            console.Profile.Width = 1_000_000;
        }
        return console;
    }
}
