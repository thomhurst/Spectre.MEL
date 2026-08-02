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
    public async Task Masks_value_pattern_in_literal_template_text()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render("Token: secret-value", "fallback", [], SpectreTheme.Monochrome, masker, collected);

        await Assert.That(result).IsEqualTo("Token: ***");
        await Assert.That(collected).Contains("secret-value");
    }

    [Test]
    public async Task Masks_value_pattern_in_fallback_message()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render(null, "Token: secret-value", [], SpectreTheme.Monochrome, masker, collected);

        await Assert.That(result).IsEqualTo("Token: ***");
        await Assert.That(collected).Contains("secret-value");
    }

    [Test]
    public async Task Value_pattern_scanning_ignores_markup_tags()
    {
        var masker = new SecretMasker([], [@"green"], 256);

        var result = MessageFormatter.Render("[green]safe[/]", "fallback", [], SpectreTheme.Monochrome, masker, allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("[green]safe[/]");
    }

    [Test]
    public async Task Value_pattern_scanning_matches_text_split_by_markup_tags()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render("secret-[red]value[/]", "fallback", [], SpectreTheme.Monochrome, masker, collected, allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("***");
        await Assert.That(collected).Contains("secret-value");
    }

    [Test]
    public async Task Value_pattern_scanning_preserves_CRLF_before_masked_text()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);

        var result = MessageFormatter.Render(null, "prefix\r\nsecret-value", [], SpectreTheme.Monochrome, masker);

        await Assert.That(result).IsEqualTo("prefix\r\n***");
    }

    [Test]
    public async Task Value_pattern_scanning_aligns_after_dropped_controls()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);

        var result = MessageFormatter.Render(null, "prefix\0\fsecret-value", [], SpectreTheme.Monochrome, masker);

        await Assert.That(result).IsEqualTo("prefix***");
    }

    [Test]
    public async Task Default_value_patterns_mask_entire_private_key_block()
    {
        const string privateKey = "-----BEGIN PRIVATE KEY-----\nYWJjZGVmZ2hpamtsbW5vcA==\n-----END PRIVATE KEY-----";
        var options = new SpectreConsoleLoggerOptions();
        var masker = new SecretMasker(options.MaskedNamePatterns, options.MaskedValuePatterns, 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render(null, $"{privateKey}\nsafe", [], SpectreTheme.Monochrome, masker, collected);

        await Assert.That(result).IsEqualTo("***\nsafe");
        await Assert.That(collected).Contains(privateKey);
    }

    [Test]
    public async Task Value_pattern_scanning_preserves_unmatched_markup_styling()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);

        var result = MessageFormatter.Render("[green]safe[/] secret-value", "fallback", [], SpectreTheme.Monochrome, masker, allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("[green]safe[/] ***");
    }

    [Test]
    public async Task Zero_length_value_pattern_masks_the_whole_literal_message()
    {
        var masker = new SecretMasker([], [@"(?=secret-\w+)"], 256);

        var result = MessageFormatter.Render(null, "prefix secret-value suffix", [], SpectreTheme.Monochrome, masker);

        await Assert.That(result).IsEqualTo("***");
    }

    [Test]
    public async Task Value_pattern_scanning_masks_link_target_attributes()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render(
            "[link=https://host/secret-value]click[/]",
            "fallback",
            [],
            SpectreTheme.Monochrome,
            masker,
            collected,
            allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("[link=https://host/***]click[/]");
        await Assert.That(collected).Contains("secret-value");
    }

    [Test]
    public async Task Zero_length_value_pattern_masks_the_whole_link_target()
    {
        var masker = new SecretMasker([], [@"(?=secret-\w+)"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render(
            "[link=https://host/secret-value]click[/]",
            "fallback",
            [],
            SpectreTheme.Monochrome,
            masker,
            collected,
            allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("[link=***]click[/]");
        await Assert.That(collected).IsEquivalentTo(["https://host/secret-value"]);
    }

    [Test]
    public async Task Value_pattern_scanning_masks_logical_escaped_link_targets()
    {
        var masker = new SecretMasker([], [@"secret\[value\]"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render(
            "[link=https://host/secret[[value]]]click[/]",
            "fallback",
            [],
            SpectreTheme.Monochrome,
            masker,
            collected,
            allowMarkupInTemplate: true);

        await Assert.That(result).IsEqualTo("[link=https://host/***]click[/]");
        await Assert.That(collected).Contains("secret[value]");
    }

    [Test]
    public async Task Value_pattern_scanning_masks_passthrough_OSC_payloads()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render(
            null,
            "\x1b]8;;https://host/secret-value\x1b\\click\x1b]8;;\x1b\\",
            [],
            SpectreTheme.Monochrome,
            masker,
            collected,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).Contains("https://host/***");
        await Assert.That(result).DoesNotContain("secret-value");
        await Assert.That(collected).Contains("secret-value");
    }

    [Test]
    public async Task Value_pattern_scanning_uses_logical_OSC_payload_text()
    {
        var masker = new SecretMasker([], [@"secret\[value\]"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render(
            null,
            "\x1b]8;;https://host/secret[value]\x1b\\click\x1b]8;;\x1b\\",
            [],
            SpectreTheme.Monochrome,
            masker,
            collected,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).Contains("https://host/***");
        await Assert.That(result).DoesNotContain("secret[[value]]");
        await Assert.That(collected).Contains("secret[value]");
    }

    [Test]
    public async Task Passthrough_masking_preserves_balanced_OSC_8_sequences()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            null,
            "\x1b]8;;https://safe\x1b\\safe secret-value\x1b]8;;\x1b\\ tail",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).Contains("safe ***");
        await Assert.That(result.Split("\x1b]]8;;", StringSplitOptions.None).Length - 1).IsEqualTo(2);
    }

    [Test]
    public async Task Value_pattern_scanning_preserves_unmatched_converted_ansi_styling()
    {
        var masker = new SecretMasker([], [@"secret-\w+"], 256);
        var style = new global::Spectre.Console.Style(global::Spectre.Console.Color.FromInt32(1)).ToMarkup();

        var result = MessageFormatter.Render("safe \x1b[31mstyled\x1b[0m secret-value", "fallback", [], SpectreTheme.Monochrome, masker);

        await Assert.That(result).IsEqualTo($"safe [{style}]styled[/] ***");
    }

    [Test]
    public async Task Value_pattern_scanning_cannot_be_bypassed_with_ansi_sequences()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);
        var collected = new List<string>();

        var result = MessageFormatter.Render("sec\x1b[31mret-value", "fallback", [], SpectreTheme.Monochrome, masker, collected, embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("***");
        await Assert.That(collected).Contains("secret-value");
    }

    [Test]
    public async Task Value_pattern_scanning_drops_unbalanced_passthrough_ansi_around_mask()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            "\x1b[31msecret-\x1b[0mvalue tail",
            "fallback",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("*** tail");
        await Assert.That(result).DoesNotContain("\x1b[");
    }

    [Test]
    public async Task Passthrough_masking_preserves_balanced_ANSI_outside_mask()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            null,
            "\x1b[32msafe\x1b[0m sec\x1b[31mret-value",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("\x1b[[32msafe\x1b[[0m ***");
    }

    [Test]
    public async Task Passthrough_masking_splits_compound_reset_and_restyle()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            null,
            "\x1b[32msafe \x1b[0;31msecret-value\x1b[0m",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("\x1b[[32msafe \x1b[[0m***");
    }

    [Test]
    public async Task Passthrough_masking_splits_direct_restyle()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            null,
            "\x1b[32msafe \x1b[31msecret-value\x1b[0m",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("\x1b[[32msafe \x1b[[0m***");
    }

    [Test]
    public async Task Passthrough_masking_restores_style_after_additive_span()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            null,
            "\x1b[32msafe \x1b[1msecret-value\x1b[22m tail\x1b[0m",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("\x1b[[32msafe \x1b[[0m***\x1b[[32m tail\x1b[[0m");
    }

    [Test]
    public async Task Passthrough_masking_preserves_style_spanning_safe_and_masked_text()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            null,
            "\x1b[31msafe secret-value tail\x1b[0m",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("\x1b[[31msafe *** tail\x1b[[0m");
    }

    [Test]
    public async Task Passthrough_ansi_masking_preserves_CRLF_index_alignment()
    {
        var masker = new SecretMasker([], [@"secret-value"], 256);

        var result = MessageFormatter.Render(
            null,
            "prefix\r\n\x1b[31msecret-value",
            [],
            SpectreTheme.Monochrome,
            masker,
            embeddedAnsi: EmbeddedAnsiMode.Passthrough);

        await Assert.That(result).IsEqualTo("prefix\r\n***");
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
