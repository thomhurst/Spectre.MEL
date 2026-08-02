using System.Globalization;
using System.Text;
using Spectre.Console;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Theme;

namespace MEL.Spectre.Rendering;

internal static class PlaceholderFormatter
{
    public static string FormatValue(object? value, string? format)
    {
        if (value is null)
        {
            return "(null)";
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(format, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    public static (string Rendered, string? UnmaskedValue, bool Masked) Render(Placeholder placeholder, string? format, SpectreTheme theme, SecretMasker masker, EmbeddedAnsiMode embeddedAnsi = EmbeddedAnsiMode.Convert)
    {
        if (masker.ShouldMask(placeholder.Name))
        {
            var unmasked = NormalizeForMasking(FormatValue(placeholder.Value, format));
            var masked = SecretMasker.Mask(placeholder.Value);
            return (Markup.Escape(masked), unmasked, true);
        }

        var formatted = FormatValue(placeholder.Value, format);

        if (masker.HasValuePatterns && placeholder.Value is string)
        {
            // Value patterns match against the ANSI-normalized text so a secret interleaved with
            // escape sequences cannot dodge masking and reappear contiguous after sanitization. The
            // normalized form is also what gets registered with the CI runner's masking.
            var plain = NormalizeForMasking(formatted);
            if (masker.ShouldMaskValue(plain))
            {
                var masked = SecretMasker.Mask(placeholder.Value);
                return (Markup.Escape(masked), plain, true);
            }
        }

        var style = theme.Placeholders.Resolve(placeholder.Name, placeholder.Value);
        var safe = AnsiSanitizer.EscapeAndSanitize(formatted, embeddedAnsi);
        if (MarkupHelper.IsPlain(style))
        {
            return (safe, null, false);
        }
        return ($"[{style.ToMarkup()}]{safe}[/]", null, false);
    }

    internal static string NormalizeForMasking(string text, bool normalizeLineEndings = false)
    {
        var normalized = AnsiSanitizer.ContainsAnsi(text)
            ? AnsiSanitizer.EscapeAndSanitize(text, EmbeddedAnsiMode.Strip, escapeMarkup: false, stripControls: false)
            : text;
        normalized = NormalizeTerminalControls(normalized);
        return normalizeLineEndings && normalized.Contains('\r')
            ? normalized.ReplaceLineEndings("\n")
            : normalized;
    }

    private static string NormalizeTerminalControls(string text)
    {
        var firstControl = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (char.IsControl(value) && value is not '\r' and not '\n' and not '\t')
            {
                firstControl = i;
                break;
            }
        }
        if (firstControl < 0) return text;

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, firstControl);
        for (var i = firstControl; i < text.Length; i++)
        {
            var value = text[i];
            if (value == '\b')
            {
                if (builder.Length > 0 && builder[^1] is not '\r' and not '\n')
                {
                    builder.Length--;
                }
                continue;
            }

            if (!char.IsControl(value) || value is '\r' or '\n' or '\t')
            {
                builder.Append(value);
            }
        }
        return builder.ToString();
    }
}
