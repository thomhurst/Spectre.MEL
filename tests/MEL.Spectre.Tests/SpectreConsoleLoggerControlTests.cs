using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Testing;
using MEL.Spectre.Theme;

namespace MEL.Spectre.Tests;

public class SpectreConsoleLoggerControlTests
{
    [Test]
    public async Task Suspend_suppresses_only_Spectre_output_until_scope_is_disposed()
    {
        var console = new TestConsole { Profile = { Width = 1_000_000 } };
        var capture = new CapturingLoggerProvider();
        await using var services = BuildServices(console, capture);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Suppression");
        var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();

        logger.LogInformation("before");
        using (control.Suspend())
        {
            await Task.Yield();
            logger.LogInformation("suppressed");

            using (control.Suspend())
            {
                logger.LogInformation("nested suppressed");
            }
        }
        logger.LogInformation("after");

        await control.FlushAsync();

        await Assert.That(console.Output).Contains("before");
        await Assert.That(console.Output).DoesNotContain("suppressed");
        await Assert.That(console.Output).Contains("after");
        await Assert.That(capture.Messages).IsEquivalentTo(["before", "suppressed", "nested suppressed", "after"]);
    }

    [Test]
    public async Task WouldRender_uses_SpectreConsole_alias_and_category_precedence()
    {
        var console = new TestConsole { Profile = { Width = 1_000_000 } };
        string? filterProviderName = null;
        await using var services = BuildServices(console, configureFilters: options =>
        {
            options.MinLevel = LogLevel.Warning;
            options.Rules.Add(new LoggerFilterRule("SpectreConsole", "Allowed", LogLevel.Debug, null));
            options.Rules.Add(new LoggerFilterRule("SpectreConsole", "FilterOnly", null, (providerName, _, _) =>
            {
                filterProviderName = providerName;
                return true;
            }));
            options.Rules.Add(new LoggerFilterRule("OtherProvider", null, LogLevel.Trace, null));
        });
        var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();

        await Assert.That(control.WouldRender("Allowed.Child", LogLevel.Information)).IsTrue();
        await Assert.That(control.WouldRender("Other", LogLevel.Information)).IsFalse();
        await Assert.That(control.WouldRender("Other", LogLevel.Warning)).IsTrue();
        await Assert.That(control.WouldRender("FilterOnly.Child", LogLevel.Trace)).IsTrue();
        await Assert.That(filterProviderName).IsEqualTo("MEL.Spectre.Provider.SpectreConsoleLoggerProvider");
        await Assert.That(control.WouldRender("Allowed.Child", LogLevel.None)).IsFalse();
    }

    private static ServiceProvider BuildServices(
        IAnsiConsole console,
        ILoggerProvider? additionalProvider = null,
        Action<LoggerFilterOptions>? configureFilters = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddSpectreConsole(options =>
            {
                options.Console = console;
                options.Theme = SpectreTheme.Monochrome;
                options.CiMode = CiMode.Off;
            });
            if (additionalProvider is not null)
            {
                builder.AddProvider(additionalProvider);
            }
        });
        if (configureFilters is not null)
        {
            services.Configure(configureFilters);
        }
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }
}
