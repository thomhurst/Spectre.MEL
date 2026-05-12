using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Testing;
using MEL.Spectre;
using MEL.Spectre.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class BackgroundWriterTests
{
    [Test]
    public async Task Drains_all_entries_on_dispose()
    {
        var captured = new TestConsole { Profile = { Width = 1_000_000 } };
        var services = new ServiceCollection()
            .AddLogging(b => b.AddSpectreConsole(o =>
            {
                o.Console = captured;
                o.Theme = SpectreTheme.Monochrome;
                o.CiMode = CiMode.Off;
            }))
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("D");
        for (var i = 0; i < 50; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        await services.DisposeAsync();

        for (var i = 0; i < 50; i++)
        {
            await Assert.That(captured.Output).Contains($"entry {i}");
        }
    }

    [Test]
    public async Task Closes_all_open_scopes_on_dispose()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            var outer = logger.BeginScope("Outer");
            var inner = logger.BeginScope("Inner");
            logger.LogInformation("x");
        });

        var openGroups = output.Split("::group::", StringSplitOptions.None).Length - 1;
        var closeGroups = output.Split("::endgroup::", StringSplitOptions.None).Length - 1;
        await Assert.That(openGroups).IsEqualTo(closeGroups);
        await Assert.That(openGroups).IsEqualTo(2);
    }

    [Test]
    public async Task Nested_scopes_open_FIFO_close_LIFO()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            using (logger.BeginScope("Outer"))
            using (logger.BeginScope("Middle"))
            using (logger.BeginScope("Inner"))
            {
                logger.LogInformation("leaf");
            }
        });

        var outerOpen = output.IndexOf("::group::Outer", StringComparison.Ordinal);
        var middleOpen = output.IndexOf("::group::Middle", StringComparison.Ordinal);
        var innerOpen = output.IndexOf("::group::Inner", StringComparison.Ordinal);
        await Assert.That(outerOpen).IsLessThan(middleOpen);
        await Assert.That(middleOpen).IsLessThan(innerOpen);

        var lastEndgroupBeforeOuterClose = output.LastIndexOf("::endgroup::", StringComparison.Ordinal);
        await Assert.That(lastEndgroupBeforeOuterClose).IsGreaterThan(innerOpen);
    }

    [Test]
    public async Task Sibling_scopes_close_previous_before_opening_next()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            using (logger.BeginScope("First"))
            {
                logger.LogInformation("a");
            }
            using (logger.BeginScope("Second"))
            {
                logger.LogInformation("b");
            }
        });

        var firstOpen = output.IndexOf("::group::First", StringComparison.Ordinal);
        var firstClose = output.IndexOf("::endgroup::", firstOpen, StringComparison.Ordinal);
        var secondOpen = output.IndexOf("::group::Second", StringComparison.Ordinal);
        await Assert.That(firstClose).IsGreaterThan(firstOpen);
        await Assert.That(secondOpen).IsGreaterThan(firstClose);
    }

    [Test]
    public async Task DropNewest_mode_drops_excess_entries_silently()
    {
        var captured = new TestConsole { Profile = { Width = 1_000_000 } };
        var services = new ServiceCollection()
            .AddLogging(b => b.AddSpectreConsole(o =>
            {
                o.Console = captured;
                o.Theme = SpectreTheme.Monochrome;
                o.CiMode = CiMode.Off;
                o.ChannelCapacity = 2;
                o.BackpressureMode = BackpressureMode.DropNewest;
            }))
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("D");

        for (var i = 0; i < 200; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        await services.DisposeAsync();

        await Assert.That(captured.Output).Contains("entry 0");
    }
}
