using System.Globalization;
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

    private static string NormalizeForMasking(string text) =>
        AnsiSanitizer.ContainsAnsi(text)
            ? AnsiSanitizer.EscapeAndSanitize(text, EmbeddedAnsiMode.Strip, escapeMarkup: false)
            : text;
}
