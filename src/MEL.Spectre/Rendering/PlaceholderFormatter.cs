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

    public static (string Rendered, string? UnmaskedValue, bool Masked) Render(Placeholder placeholder, string? format, SpectreTheme theme, SecretMasker masker)
    {
        if (masker.ShouldMask(placeholder.Name))
        {
            var unmasked = FormatValue(placeholder.Value, format);
            var masked = SecretMasker.Mask(placeholder.Value);
            return (Markup.Escape(masked), unmasked, true);
        }

        var formatted = FormatValue(placeholder.Value, format);
        var style = theme.Placeholders.Resolve(placeholder.Name, placeholder.Value);
        if (MarkupHelper.IsPlain(style))
        {
            return (Markup.Escape(formatted), null, false);
        }
        return ($"[{style.ToMarkup()}]{Markup.Escape(formatted)}[/]", null, false);
    }
}
