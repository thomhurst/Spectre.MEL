using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Testing;
using Spectre.MEL;
using Spectre.MEL.Theme;

namespace Spectre.MEL.Tests;

internal static class LogTestHarness
{
    public static (TestConsole Console, ServiceProvider Services, ILogger Logger) Build(
        CiMode ciMode,
        Action<SpectreConsoleLoggerOptions>? configure = null,
        string category = "Test")
    {
        var captured = new TestConsole();
        captured.Profile.Width = 1_000_000;

        var services = new ServiceCollection()
            .AddLogging(b => b
                .SetMinimumLevel(LogLevel.Trace)
                .AddSpectreConsole(o =>
                {
                    o.Console = captured;
                    o.Theme = SpectreTheme.Monochrome;
                    o.CiMode = ciMode;
                    configure?.Invoke(o);
                }))
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
        return (captured, services, logger);
    }

    public static async Task<string> CaptureAsync(
        CiMode ciMode,
        Func<ILogger, Task> work,
        Action<SpectreConsoleLoggerOptions>? configure = null,
        string category = "Test")
    {
        var (console, services, logger) = Build(ciMode, configure, category);
        try
        {
            await work(logger);
        }
        finally
        {
            await services.DisposeAsync();
        }
        return console.Output;
    }

    public static Task<string> CaptureAsync(
        CiMode ciMode,
        Action<ILogger> work,
        Action<SpectreConsoleLoggerOptions>? configure = null,
        string category = "Test")
        => CaptureAsync(ciMode, l => { work(l); return Task.CompletedTask; }, configure, category);
}
