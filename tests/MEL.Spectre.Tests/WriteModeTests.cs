using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Testing;
using MEL.Spectre.Provider;
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

    [Test]
    public async Task Synchronous_FlushAsync_honors_cancellation_while_write_lock_is_held()
    {
        var console = new TestConsole { Profile = { Width = 1_000_000 } };
        var services = BuildServices(console, WriteMode.Synchronous);
        var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var holder = Task.Run(() =>
        {
            lock (control.SynchronizationLock)
            {
                entered.Set();
                release.Wait();
            }
        });

        try
        {
            entered.Wait();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.That(async () => await control.FlushAsync(cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        finally
        {
            release.Set();
            await holder;
            await services.DisposeAsync();
        }
    }

    [Test]
    public async Task FlushAsync_faults_when_disposal_drain_times_out()
    {
        var console = new BlockingAnsiConsole();
        var services = BuildServices(console, configure: options =>
        {
            options.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(50);
            options.EnqueueWaitTimeout = TimeSpan.FromMilliseconds(50);
        });

        try
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TimeoutFlush");
            var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();
            logger.LogInformation("blocked");

            var flush = control.FlushAsync();
            await services.DisposeAsync();

            await Assert.That(flush.IsFaulted).IsTrue();
            await Assert.That(async () => await flush).Throws<TimeoutException>();
            await Assert.That(control.FlushAsync().IsFaulted).IsTrue();
        }
        finally
        {
            console.Release();
            await services.DisposeAsync();
        }
    }

    [Test]
    public async Task Canceled_background_flushes_remove_their_waiters()
    {
        var console = new BlockingAnsiConsole();
        var services = BuildServices(console);

        try
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CanceledFlush");
            var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();
            var writer = GetWriter<BackgroundWriter>(services);
            logger.LogInformation("blocked");

            for (var i = 0; i < 100; i++)
            {
                using var cancellation = new CancellationTokenSource();
                var flush = control.FlushAsync(cancellation.Token);
                cancellation.Cancel();
                await Assert.That(async () => await flush).Throws<OperationCanceledException>();
            }

            await Assert.That(writer.PendingFlushWaiterCount).IsEqualTo(0);
        }
        finally
        {
            console.Release();
            await services.DisposeAsync();
        }
    }

    [Test]
    public async Task Synchronous_FlushAsync_waits_for_log_already_blocked_on_write_lock()
    {
        var console = new TestConsole { Profile = { Width = 1_000_000 } };
        var services = BuildServices(console, WriteMode.Synchronous);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SyncFlush");
        var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();
        var writer = GetWriter<SynchronousWriter>(services);
        Task log;
        Task flush;

        lock (control.SynchronizationLock)
        {
            log = Task.Run(() => logger.LogInformation("waiting log"));
            if (!SpinWait.SpinUntil(() => writer.PendingEntryCount == 1, TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The synchronous log did not reach the writer.");
            }

            flush = control.FlushAsync();
            if (flush.IsCompleted)
            {
                throw new InvalidOperationException("Flush completed before the earlier log acquired the write lock.");
            }
        }

        await log;
        await flush;
        await Assert.That(console.Output).Contains("waiting log");
        await services.DisposeAsync();
    }

    private static TWriter GetWriter<TWriter>(IServiceProvider services)
        where TWriter : class, ILogEntryWriter
    {
        var provider = services.GetServices<ILoggerProvider>().OfType<SpectreConsoleLoggerProvider>().Single();
        var field = typeof(SpectreConsoleLoggerProvider).GetField(
            "_writer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (TWriter)field.GetValue(provider)!;
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
