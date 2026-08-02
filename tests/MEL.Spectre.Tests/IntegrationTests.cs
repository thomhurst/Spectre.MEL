using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console.Testing;
using MEL.Spectre;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class IntegrationTests
{
    [Test]
    public async Task Binds_provider_options_from_logging_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:SpectreConsole:Template"] = "configured {Message}",
                ["Logging:SpectreConsole:CiMode"] = nameof(CiMode.GitLabCi),
                ["Logging:SpectreConsole:WriteMode"] = nameof(WriteMode.Synchronous),
                ["Logging:SpectreConsole:IncludeScopes"] = "false",
            })
            .Build();

        await using var services = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddSpectreConsole();
            })
            .BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<SpectreConsoleLoggerOptions>>().Value;
        await Assert.That(options.Template).IsEqualTo("configured {Message}");
        await Assert.That(options.CiMode).IsEqualTo(CiMode.GitLabCi);
        await Assert.That(options.WriteMode).IsEqualTo(WriteMode.Synchronous);
        await Assert.That(options.IncludeScopes).IsFalse();
    }

    [Test]
    public async Task Code_configuration_overrides_provider_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:SpectreConsole:Template"] = "configured {Message}",
            })
            .Build();

        await using var services = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddSpectreConsole(options => options.Template = "code {Message}");
            })
            .BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<SpectreConsoleLoggerOptions>>().Value;
        await Assert.That(options.Template).IsEqualTo("code {Message}");
    }


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
