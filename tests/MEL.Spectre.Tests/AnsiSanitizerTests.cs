using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Testing;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Rendering;
using MEL.Spectre.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class AnsiSanitizerTests
{
    private const string Esc = "\u001b";

    [Test]
    public async Task Strip_removes_sgr_sequences()
    {
        var result = AnsiSanitizer.EscapeAndSanitize($"{Esc}[32mpassed{Esc}[0m in 3s", EmbeddedAnsiMode.Strip);
        await Assert.That(result).IsEqualTo("passed in 3s");
    }

    [Test]
    public async Task Passthrough_keeps_raw_sequences_and_only_markup_escapes()
    {
        var text = $"{Esc}[32mpassed{Esc}[0m";
        var result = AnsiSanitizer.EscapeAndSanitize(text, EmbeddedAnsiMode.Passthrough);
        await Assert.That(result).IsEqualTo(Markup.Escape(text));
    }

    [Test]
    public async Task Convert_translates_basic_color_to_markup()
    {
        var result = AnsiSanitizer.EscapeAndSanitize($"{Esc}[32mgreen{Esc}[0m plain", EmbeddedAnsiMode.Convert);
        var expectedTag = new Style(Color.FromInt32(2)).ToMarkup();
        await Assert.That(result).IsEqualTo($"[{expectedTag}]green[/] plain");
    }

    [Test]
    public async Task Convert_translates_bold_combined_with_color()
    {
        var result = AnsiSanitizer.EscapeAndSanitize($"{Esc}[1;31mfail{Esc}[0m", EmbeddedAnsiMode.Convert);
        var expectedTag = new Style(Color.FromInt32(1), decoration: Decoration.Bold).ToMarkup();
        await Assert.That(result).IsEqualTo($"[{expectedTag}]fail[/]");
    }

    [Test]
    public async Task Convert_translates_256_palette_and_truecolor()
    {
        var palette = AnsiSanitizer.EscapeAndSanitize($"{Esc}[38;5;229mtext{Esc}[0m", EmbeddedAnsiMode.Convert);
        await Assert.That(palette).IsEqualTo($"[{new Style(Color.FromInt32(229)).ToMarkup()}]text[/]");

        var truecolor = AnsiSanitizer.EscapeAndSanitize($"{Esc}[38;2;255;100;0mtext{Esc}[0m", EmbeddedAnsiMode.Convert);
        await Assert.That(truecolor).IsEqualTo($"[{new Style(new Color(255, 100, 0)).ToMarkup()}]text[/]");
    }

    [Test]
    public async Task Convert_translates_colon_form_extended_colors()
    {
        var expected = $"[{new Style(new Color(255, 100, 0)).ToMarkup()}]text[/]";

        // ITU T.416 form with an empty color-space field
        var withColorSpace = AnsiSanitizer.EscapeAndSanitize($"{Esc}[38:2::255:100:0mtext{Esc}[0m", EmbeddedAnsiMode.Convert);
        await Assert.That(withColorSpace).IsEqualTo(expected);

        var withoutColorSpace = AnsiSanitizer.EscapeAndSanitize($"{Esc}[38:2:255:100:0mtext{Esc}[0m", EmbeddedAnsiMode.Convert);
        await Assert.That(withoutColorSpace).IsEqualTo(expected);

        var palette = AnsiSanitizer.EscapeAndSanitize($"{Esc}[38:5:229mtext{Esc}[0m", EmbeddedAnsiMode.Convert);
        await Assert.That(palette).IsEqualTo($"[{new Style(Color.FromInt32(229)).ToMarkup()}]text[/]");

        // colon run is self-delimiting: the following ';'-separated parameter still applies
        var mixed = AnsiSanitizer.EscapeAndSanitize($"{Esc}[38:5:229;1mtext{Esc}[0m", EmbeddedAnsiMode.Convert);
        await Assert.That(mixed).IsEqualTo($"[{new Style(Color.FromInt32(229), decoration: Decoration.Bold).ToMarkup()}]text[/]");
    }

    [Test]
    public async Task Eight_bit_c1_sequences_are_handled()
    {
        // 8-bit CSI SGR converts like its ESC[ equivalent
        var csi = AnsiSanitizer.EscapeAndSanitize("\u009b32mgreen\u009b0m", EmbeddedAnsiMode.Convert);
        await Assert.That(csi).IsEqualTo($"[{new Style(Color.FromInt32(2)).ToMarkup()}]green[/]");

        // C1 OSC / DCS payloads run to the C1 ST and are removed entirely
        var osc = AnsiSanitizer.EscapeAndSanitize("\u009d0;title\u009cafter", EmbeddedAnsiMode.Convert);
        await Assert.That(osc).IsEqualTo("after");

        var dcs = AnsiSanitizer.EscapeAndSanitize("\u0090payload\u009cafter", EmbeddedAnsiMode.Convert);
        await Assert.That(dcs).IsEqualTo("after");

        // a stray ST without an opener is dropped
        var strayTerminator = AnsiSanitizer.EscapeAndSanitize("a\u009cb", EmbeddedAnsiMode.Convert);
        await Assert.That(strayTerminator).IsEqualTo("ab");
    }

    [Test]
    public async Task Convert_produces_balanced_markup_for_issue_sample()
    {
        var text = $"{Esc}[38;5;229m{Esc}[32mTest run summary: Passed!{Esc}[90m - {Esc}[m/home/runner/work";
        var result = AnsiSanitizer.EscapeAndSanitize(text, EmbeddedAnsiMode.Convert);

        await Assert.That(result).DoesNotContain(Esc);
        _ = new Markup(result); // throws if the produced markup is malformed or unbalanced
        await Assert.That(Markup.Remove(result)).IsEqualTo("Test run summary: Passed! - /home/runner/work");
    }

    [Test]
    public async Task Embedded_reset_closes_only_embedded_style_and_outer_style_resumes()
    {
        var converted = AnsiSanitizer.EscapeAndSanitize($"child {Esc}[31mred{Esc}[0m tail", EmbeddedAnsiMode.Convert);
        var console = new TestConsole().EmitAnsiSequences();
        console.Markup($"[bold]{converted}[/]");

        var output = console.Output;
        var afterEmbedded = output[(output.IndexOf("red", StringComparison.Ordinal) + 3)..];
        await Assert.That(afterEmbedded).Contains($"{Esc}[1m"); // outer bold re-applied for " tail"
    }

    [Test]
    public async Task Brackets_in_converted_text_are_escaped()
    {
        var result = AnsiSanitizer.EscapeAndSanitize($"{Esc}[32m[ok]{Esc}[0m", EmbeddedAnsiMode.Convert);
        _ = new Markup(result);
        await Assert.That(Markup.Remove(result)).IsEqualTo("[ok]");
    }

    [Test]
    public async Task Osc_and_hyperlink_sequences_are_removed()
    {
        var title = AnsiSanitizer.EscapeAndSanitize($"{Esc}]0;window title\aafter", EmbeddedAnsiMode.Convert);
        await Assert.That(title).IsEqualTo("after");

        var hyperlink = AnsiSanitizer.EscapeAndSanitize($"{Esc}]8;;https://x.example{Esc}\\link{Esc}]8;;{Esc}\\", EmbeddedAnsiMode.Convert);
        await Assert.That(hyperlink).IsEqualTo("link");
    }

    [Test]
    public async Task Bel_terminates_only_osc_payloads()
    {
        // BEL inside a DCS/APC payload is data, not a terminator — the payload runs to ST
        var dcs = AnsiSanitizer.EscapeAndSanitize($"{Esc}Pdata\ahidden{Esc}\\after", EmbeddedAnsiMode.Convert);
        await Assert.That(dcs).IsEqualTo("after");

        var apc = AnsiSanitizer.EscapeAndSanitize($"{Esc}_data\ahidden{Esc}\\after", EmbeddedAnsiMode.Convert);
        await Assert.That(apc).IsEqualTo("after");
    }

    [Test]
    public async Task Cursor_and_erase_sequences_are_removed_in_convert_mode()
    {
        var result = AnsiSanitizer.EscapeAndSanitize($"{Esc}[2K{Esc}[1Gprogress 100%", EmbeddedAnsiMode.Convert);
        await Assert.That(result).IsEqualTo("progress 100%");
    }

    [Test]
    public async Task Stray_control_characters_are_removed_but_newlines_and_tabs_kept()
    {
        var result = AnsiSanitizer.EscapeAndSanitize($"{Esc}[32ma\r\nb\tc\bd{Esc}[0m", EmbeddedAnsiMode.Strip);
        await Assert.That(result).IsEqualTo("a\nb\tcd");
    }

    [Test]
    public async Task Truncated_and_dangling_sequences_do_not_throw()
    {
        await Assert.That(AnsiSanitizer.EscapeAndSanitize($"abc{Esc}", EmbeddedAnsiMode.Convert)).IsEqualTo("abc");
        await Assert.That(AnsiSanitizer.EscapeAndSanitize($"abc{Esc}[32", EmbeddedAnsiMode.Convert)).IsEqualTo("abc");
        await Assert.That(AnsiSanitizer.EscapeAndSanitize($"abc{Esc}]0;title", EmbeddedAnsiMode.Convert)).IsEqualTo("abc");
    }

    [Test]
    public async Task Malformed_csi_does_not_swallow_the_following_sequence()
    {
        // The truncated CSI is dropped; the reset that cut it short must still be consumed
        var reset = AnsiSanitizer.EscapeAndSanitize($"x{Esc}[31{Esc}[0my", EmbeddedAnsiMode.Convert);
        await Assert.That(reset).IsEqualTo("xy");

        var color = AnsiSanitizer.EscapeAndSanitize($"x{Esc}[31{Esc}[32mgreen{Esc}[0m", EmbeddedAnsiMode.Convert);
        await Assert.That(color).IsEqualTo($"x[{new Style(Color.FromInt32(2)).ToMarkup()}]green[/]");

        // A CSI cut short by a C0 control abandons the sequence and keeps the newline
        var newline = AnsiSanitizer.EscapeAndSanitize($"x{Esc}[31\ny", EmbeddedAnsiMode.Convert);
        await Assert.That(newline).IsEqualTo("x\ny");
    }

    [Test]
    public async Task Reset_in_unmatched_brace_tail_closes_style_opened_before_it()
    {
        var masker = new SecretMasker(new SpectreConsoleLoggerOptions().MaskedNamePatterns, 256);
        var result = MessageFormatter.Render(
            $"{Esc}[31m{{err{Esc}[0m tail",
            "fb",
            [],
            SpectreTheme.Monochrome,
            masker);

        var tag = new Style(Color.FromInt32(1)).ToMarkup();
        await Assert.That(result).IsEqualTo($"[{tag}]{{err[/] tail");
    }

    [Test]
    public async Task Message_format_string_with_ansi_is_converted()
    {
        var masker = new SecretMasker(new SpectreConsoleLoggerOptions().MaskedNamePatterns, 256);
        var result = MessageFormatter.Render(
            $"{Esc}[32mok{Esc}[0m {{Count}} done",
            "fb",
            [new Placeholder("Count", 3, typeof(int))],
            SpectreTheme.Monochrome,
            masker);

        var expectedTag = new Style(Color.FromInt32(2)).ToMarkup();
        await Assert.That(result).IsEqualTo($"[{expectedTag}]ok[/] 3 done");
    }

    [Test]
    public async Task Logger_sanitizes_child_process_ansi_by_default()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
            logger.LogInformation("Child said {Line}", $"{Esc}[38;5;229m{Esc}[32mPassed!{Esc}[m total: 25{Esc}[0m"));

        await Assert.That(output).DoesNotContain(Esc);
        await Assert.That(output).Contains("Passed! total: 25");
    }

    [Test]
    public async Task Logger_sanitizes_ansi_in_direct_format_string()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
#pragma warning disable CA2254 // deliberately logging a non-constant, pre-rendered child-process line
            logger.LogInformation($"{Esc}[38;5;229m{Esc}[mtotal: 25{Esc}[0m"));
#pragma warning restore CA2254

        await Assert.That(output).DoesNotContain(Esc);
        await Assert.That(output).Contains("total: 25");
    }

    [Test]
    public async Task Passthrough_mode_preserves_raw_sequences()
    {
        var output = await LogTestHarness.CaptureAsync(
            CiMode.Off,
            logger => logger.LogInformation("x {Line} y", $"{Esc}[32mgreen{Esc}[0m"),
            o => o.EmbeddedAnsi = EmbeddedAnsiMode.Passthrough);

        await Assert.That(output).Contains($"{Esc}[32m");
    }

    [Test]
    public async Task GitHub_annotation_payload_is_free_of_ansi()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
            logger.LogError("Failed: {Line}", $"{Esc}[31mboom{Esc}[0m"));

        var annotationLine = output.Split('\n').First(l => l.StartsWith("::error::", StringComparison.Ordinal));
        await Assert.That(annotationLine).DoesNotContain(Esc);
        await Assert.That(annotationLine).Contains("boom");
    }
}
