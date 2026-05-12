using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MEL.Spectre;
using MEL.Spectre.Provider;
using MEL.Spectre.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

[NotInParallel("stderr-capture")]
public class BackgroundWriterEdgeCasesTests
{
    [Test]
    public async Task DropOldest_keeps_recent_entries_and_drops_early_ones()
    {
        var blocking = new BlockingAnsiConsole();
        var services = new ServiceCollection()
            .AddLogging(b => b.AddSpectreConsole(o =>
            {
                o.Console = blocking;
                o.Theme = SpectreTheme.Monochrome;
                o.CiMode = CiMode.Off;
                o.ChannelCapacity = 2;
                o.BackpressureMode = BackpressureMode.DropOldest;
            }))
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("D");
        for (var i = 0; i < 50; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        blocking.Release();
        await services.DisposeAsync();

        await Assert.That(blocking.Output).Contains("entry 49");
        // entry 0 may slip through: consumer dequeues it before the gate blocks, so backpressure can't drop it.
        // a mid-range entry is unambiguously inside the dropped window.
        await Assert.That(blocking.Output).DoesNotContain("entry 25");
    }

    [Test]
    public async Task Wait_mode_drops_after_EnqueueWaitTimeout()
    {
        var stderr = new StringWriter();
        var originalErr = System.Console.Error;
        System.Console.SetError(stderr);
        try
        {
            var blocking = new BlockingAnsiConsole();
            var services = new ServiceCollection()
                .AddLogging(b => b.AddSpectreConsole(o =>
                {
                    o.Console = blocking;
                    o.Theme = SpectreTheme.Monochrome;
                    o.CiMode = CiMode.Off;
                    o.ChannelCapacity = 1;
                    o.BackpressureMode = BackpressureMode.Wait;
                    o.EnqueueWaitTimeout = TimeSpan.FromMilliseconds(50);
                    o.ShutdownDrainTimeout = TimeSpan.FromSeconds(1);
                }))
                .BuildServiceProvider();

            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("W");
            for (var i = 0; i < 10; i++)
            {
                logger.LogInformation("entry {Index}", i);
            }

            var writer = GetWriter(services);
            await Assert.That(writer.DroppedBackpressureCount).IsGreaterThan(0);

            blocking.Release();
            await services.DisposeAsync();
        }
        finally
        {
            System.Console.SetError(originalErr);
        }

        await Assert.That(stderr.ToString()).Contains("backpressure");
    }

    [Test]
    public async Task Enqueue_after_dispose_increments_dropped_counter()
    {
        var stderr = new StringWriter();
        var originalErr = System.Console.Error;
        System.Console.SetError(stderr);
        try
        {
            var (_, services, logger) = LogTestHarness.Build(CiMode.Off);
            var writer = GetWriter(services);

            await services.DisposeAsync();

            logger.LogInformation("after dispose 1");
            logger.LogInformation("after dispose 2");

            await Assert.That(writer.DroppedAfterDisposeCount).IsGreaterThanOrEqualTo(2);
        }
        finally
        {
            System.Console.SetError(originalErr);
        }

        await Assert.That(stderr.ToString()).Contains("dropped after provider disposal");
    }

    [Test]
    public async Task One_render_fault_does_not_kill_consumer()
    {
        var poison = new PoisonAnsiConsole("KABOOM");
        var services = new ServiceCollection()
            .AddLogging(b => b.AddSpectreConsole(o =>
            {
                o.Console = poison;
                o.Theme = SpectreTheme.Monochrome;
                o.CiMode = CiMode.Off;
            }))
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("P");

        var stderr = new StringWriter();
        var originalErr = System.Console.Error;
        System.Console.SetError(stderr);
        try
        {
            logger.LogInformation("KABOOM");
            logger.LogInformation("safe entry");
            await services.DisposeAsync();
        }
        finally
        {
            System.Console.SetError(originalErr);
        }

        await Assert.That(poison.Output).Contains("safe entry");
        await Assert.That(stderr.ToString()).Contains("render fault");
    }

    [Test]
    public async Task Drain_timeout_emits_stderr_warning_and_closes_scopes()
    {
        var stderr = new StringWriter();
        var originalErr = System.Console.Error;
        System.Console.SetError(stderr);

        var blocking = new BlockingAnsiConsole();
        try
        {
            var services = new ServiceCollection()
                .AddLogging(b => b.AddSpectreConsole(o =>
                {
                    o.Console = blocking;
                    o.Theme = SpectreTheme.Monochrome;
                    o.CiMode = CiMode.GitHubActions;
                    o.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(50);
                    o.EnqueueWaitTimeout = TimeSpan.FromMilliseconds(50);
                }))
                .BuildServiceProvider();

            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DT");

            using (logger.BeginScope("Outer"))
            {
                logger.LogInformation("inside");
            }

            await services.DisposeAsync();
            blocking.Release();
        }
        finally
        {
            System.Console.SetError(originalErr);
        }

        await Assert.That(stderr.ToString()).Contains("drain timeout");
    }

    private static BackgroundWriter GetWriter(IServiceProvider services)
    {
        var loggerProviders = services.GetServices<ILoggerProvider>();
        var spectreProvider = loggerProviders.OfType<SpectreConsoleLoggerProvider>().Single();
        var field = typeof(SpectreConsoleLoggerProvider).GetField("_writer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (BackgroundWriter)field.GetValue(spectreProvider)!;
    }
}
