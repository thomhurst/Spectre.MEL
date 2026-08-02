using MEL.Spectre.Provider;
using Microsoft.Extensions.Logging;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class StateReaderTests
{
    [Test]
    public async Task Extracts_original_format_and_named_placeholders()
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("UserId", 42),
            new("Email", "a@b.com"),
            new("{OriginalFormat}", "User {UserId} email {Email}"),
        };

        var (originalFormat, placeholders, allowMarkup) = StateReader.Extract(state);

        await Assert.That(originalFormat).IsEqualTo("User {UserId} email {Email}");
        await Assert.That(placeholders).Count().IsEqualTo(2);
        await Assert.That(placeholders[0].Name).IsEqualTo("UserId");
        await Assert.That(placeholders[0].Value).IsEqualTo(42);
        await Assert.That(placeholders[1].Name).IsEqualTo("Email");
        await Assert.That(allowMarkup).IsFalse();
    }

    [Test]
    public async Task Returns_empty_for_state_without_format()
    {
        var (originalFormat, placeholders, allowMarkup) = StateReader.Extract("just a string");
        await Assert.That(originalFormat).IsNull();
        await Assert.That(placeholders).IsEmpty();
        await Assert.That(allowMarkup).IsFalse();
    }

    [Test]
    public async Task Captures_runtime_value_types()
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Count", 7),
            new("Rate", 0.5),
            new("{OriginalFormat}", "x"),
        };

        var (_, placeholders, _) = StateReader.Extract(state);
        await Assert.That(placeholders[0].ValueType).IsEqualTo(typeof(int));
        await Assert.That(placeholders[1].ValueType).IsEqualTo(typeof(double));
    }

    [Test]
    public async Task Extracts_markup_state_without_exposing_placeholder()
    {
        var state = new TestMarkupState("[green]ok[/]");

        var (originalFormat, placeholders, allowMarkup) = StateReader.Extract(state);

        await Assert.That(originalFormat).IsNull();
        await Assert.That(placeholders).IsEmpty();
        await Assert.That(allowMarkup).IsTrue();
    }

    [Test]
    public async Task LogMarkup_does_not_expose_provider_marker_as_structured_state()
    {
        var logger = new CapturingLogger();

        logger.LogMarkup("[green]ok[/]");

        await Assert.That(logger.State is IEnumerable<KeyValuePair<string, object?>>).IsFalse();
        await Assert.That(logger.State?.ToString()).IsEqualTo("[green]ok[/]");
    }

    private sealed class CapturingLogger : ILogger
    {
        public object? State { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            State = state;
    }

    private sealed record TestMarkupState(string Markup) : IMarkupLogState;
}
