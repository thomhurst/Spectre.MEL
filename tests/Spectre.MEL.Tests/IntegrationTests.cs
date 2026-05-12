using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Testing;
using Spectre.MEL;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class IntegrationTests
{
    [Test]
    public async Task End_to_end_logger_produces_expected_text()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 1_000_000;

        await using (var sp = new ServiceCollection()
            .AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddSpectreConsole(o =>
                {
                    o.Console = captured;
                    o.Theme = Theme.SpectreTheme.Monochrome;
                    o.CiMode = CiMode.Off;
                    o.IncludeScopes = false;
                }))
            .BuildServiceProvider())
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
            logger.LogInformation("Hello {Name}", "world");
            logger.LogWarning("Bad number {N}", 42);

            await sp.DisposeAsync();
        }

        var output = captured.Output;
        await Assert.That(output).Contains("Hello world");
        await Assert.That(output).Contains("Bad number 42");
        await Assert.That(output).Contains("INFO");
        await Assert.That(output).Contains("WARN");
    }

    [Test]
    public async Task GitHub_actions_mode_emits_group_markers()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 1_000_000;

        await using (var sp = new ServiceCollection()
            .AddLogging(builder => builder
                .AddSpectreConsole(o =>
                {
                    o.Console = captured;
                    o.Theme = Theme.SpectreTheme.Monochrome;
                    o.CiMode = CiMode.GitHubActions;
                }))
            .BuildServiceProvider())
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
            using (logger.BeginScope("Outer"))
            {
                logger.LogInformation("inside");
            }

            await sp.DisposeAsync();
        }

        var output = captured.Output;
        await Assert.That(output).Contains("::group::Outer");
        await Assert.That(output).Contains("::endgroup::");
    }

    [Test]
    public async Task GitHub_actions_emits_add_mask_for_secrets()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 1_000_000;

        await using (var sp = new ServiceCollection()
            .AddLogging(builder => builder
                .AddSpectreConsole(o =>
                {
                    o.Console = captured;
                    o.Theme = Theme.SpectreTheme.Monochrome;
                    o.CiMode = CiMode.GitHubActions;
                }))
            .BuildServiceProvider())
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
            logger.LogInformation("Auth: {Authorization}", "Bearer xyz");

            await sp.DisposeAsync();
        }

        var output = captured.Output;
        await Assert.That(output).Contains("::add-mask::Bearer xyz");
        await Assert.That(output).Contains("Auth: ***");
    }
}
