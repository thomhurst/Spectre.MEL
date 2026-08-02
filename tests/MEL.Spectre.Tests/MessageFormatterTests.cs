using Microsoft.Extensions.Logging;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Rendering;
using MEL.Spectre.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class MessageFormatterTests
{
    private static SecretMasker NewMasker() => new(new SpectreConsoleLoggerOptions().MaskedNamePatterns, 256);

    [Test]
    public async Task Substitutes_named_placeholders()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var placeholders = new[]
        {
            new Placeholder("UserId", 42, typeof(int)),
            new Placeholder("Email", "a@b", typeof(string)),
        };

        var result = MessageFormatter.Render("User {UserId} email {Email}", "fallback", placeholders, theme, masker);
        await Assert.That(result).IsEqualTo("User 42 email a@b");
    }

    [Test]
    public async Task Masks_secret_named_placeholder()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var placeholders = new[]
        {
            new Placeholder("Authorization", "Bearer xyz", typeof(string)),
        };

        var collected = new List<string>();
        var result = MessageFormatter.Render("Header {Authorization}", "fallback", placeholders, theme, masker, collected);
        await Assert.That(result).IsEqualTo("Header ***");
        await Assert.That(collected).Contains("Bearer xyz");
    }

    [Test]
    public async Task Escapes_markup_brackets_in_literal()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var result = MessageFormatter.Render("[group] {Value}", "fb", [new Placeholder("Value", 1, typeof(int))], theme, masker);
        await Assert.That(result).IsEqualTo("[[group]] 1");
    }

    [Test]
    public async Task Collapses_message_template_brace_escapes()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var capture = new CapturingLogger();
        capture.LogInformation("use {{0}} and }} braces with {Value}", 42);
        var (originalFormat, placeholders, _) = StateReader.Extract(capture.State);

        var result = MessageFormatter.Render(originalFormat, capture.Message!, placeholders, theme, masker);

        await Assert.That(result).IsEqualTo(capture.Message);
    }

    [Test]
    public async Task Masked_placeholder_renders_safe_markup()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var placeholders = new[] { new Placeholder("Token", "abc", typeof(string)) };

        var result = MessageFormatter.Render("{Token}", "fb", placeholders, theme, masker);

        await Assert.That(result).Contains("***");
        _ = new global::Spectre.Console.Markup(result);
    }

    [Test]
    public async Task Unmasked_value_with_brackets_is_escaped_for_markup_safety()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var value = new BracketyToString();
        var placeholders = new[] { new Placeholder("Value", value, typeof(BracketyToString)) };

        var result = MessageFormatter.Render("{Value}", "fb", placeholders, theme, masker);

        await Assert.That(result).Contains("[[REDACTED]]");
        _ = new global::Spectre.Console.Markup(result);
    }

    [Test]
    public async Task Allows_markup_in_template_when_opted_in()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var placeholders = new[] { new Placeholder("Name", "auth", typeof(string)) };

        var result = MessageFormatter.Render("[green]✓[/] Module {Name}", "fb", placeholders, theme, masker, allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("[green]✓[/] Module auth");
    }

    [Test]
    public async Task Markup_pass_through_still_escapes_brackety_placeholder_value()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var placeholders = new[] { new Placeholder("Value", new BracketyToString(), typeof(BracketyToString)) };

        var result = MessageFormatter.Render("[cyan]{Value}[/]", "fb", placeholders, theme, masker, allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("[cyan][[REDACTED]][/]");
        _ = new global::Spectre.Console.Markup(result);
    }

    [Test]
    public async Task Unmatched_brace_tail_keeps_markup_when_opted_in()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();

        var result = MessageFormatter.Render("{unmatched [bold]text[/]", "fb", [], theme, masker, allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("{unmatched [bold]text[/]");
    }

    private sealed class BracketyToString
    {
        public override string ToString() => "[REDACTED]";
    }

    private sealed class CapturingLogger : ILogger
    {
        public object? State { get; private set; }

        public string? Message { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            State = state;
            Message = formatter(state, exception);
        }
    }
}
