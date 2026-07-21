using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Testing;
using MEL.Spectre.Theme;

namespace MEL.Spectre.Tests;

[NotInParallel("stderr-capture")]
public class WriteModeTests
{
    [Test]
    public async Task FlushAsync_renders_every_entry_enqueued_before_call()
    {
        var console = new TestConsole { Profile = { Width = 1_000_000 } };
        var services = BuildServices(console);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Flush");
        var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();

        for (var i = 0; i < 50; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        await control.FlushAsync();

        for (var i = 0; i < 50; i++)
        {
            await Assert.That(console.Output).Contains($"entry {i}");
        }

        await services.DisposeAsync();
    }

    [Test]
    public async Task Synchronous_mode_preserves_program_order_before_Log_returns()
    {
        var console = new TestConsole { Profile = { Width = 1_000_000 } };
        var services = BuildServices(console, WriteMode.Synchronous);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Sync");
        var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();

        logger.LogInformation("first");
        await Assert.That(console.Output).Contains("first");

        lock (control.SynchronizationLock)
        {
            console.Profile.Out.Writer.WriteLine("direct");
        }
        logger.LogInformation("second");

        var first = console.Output.IndexOf("first", StringComparison.Ordinal);
        var direct = console.Output.IndexOf("direct", StringComparison.Ordinal);
        var second = console.Output.IndexOf("second", StringComparison.Ordinal);
        await Assert.That(first).IsLessThan(direct);
        await Assert.That(direct).IsLessThan(second);

        await services.DisposeAsync();
    }

    [Test]
    [Arguments(BackpressureMode.DropNewest)]
    [Arguments(BackpressureMode.DropOldest)]
    public async Task FlushAsync_completes_when_backpressure_drops_entries(BackpressureMode backpressureMode)
    {
        var console = new BlockingAnsiConsole();
        var services = BuildServices(console, configure: options =>
        {
            options.ChannelCapacity = 1;
            options.BackpressureMode = backpressureMode;
        });

        try
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DropFlush");
            var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();
            for (var i = 0; i < 100; i++)
            {
                logger.LogInformation("entry {Index}", i);
            }

            var flush = control.FlushAsync();
            await Assert.That(flush.IsCompleted).IsFalse();

            console.Release();
            await flush.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            console.Release();
            await services.DisposeAsync();
        }
    }

    private static ServiceProvider BuildServices(
        IAnsiConsole console,
        WriteMode writeMode = WriteMode.Background,
        Action<SpectreConsoleLoggerOptions>? configure = null) =>
        new ServiceCollection()
            .AddLogging(builder => builder.AddSpectreConsole(options =>
            {
                options.Console = console;
                options.Theme = SpectreTheme.Monochrome;
                options.CiMode = CiMode.Off;
                options.WriteMode = writeMode;
                configure?.Invoke(options);
            }))
            .BuildServiceProvider();
}
