using System.Text;
using Spectre.Console;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Theme;

namespace MEL.Spectre.Rendering;

internal static class MessageFormatter
{
    public static string Render(string? originalFormat, string fallback, Placeholder[] placeholders, SpectreTheme theme, SecretMasker masker, List<string>? collectMaskValues = null, bool allowMarkupInTemplate = false, EmbeddedAnsiMode embeddedAnsi = EmbeddedAnsiMode.Convert)
    {
        if (string.IsNullOrEmpty(originalFormat))
        {
            return AnsiSanitizer.EscapeAndSanitize(fallback, embeddedAnsi, escapeMarkup: !allowMarkupInTemplate);
        }

        var builder = new StringBuilder(originalFormat.Length + 32);
        var i = 0;
        var nextPositional = 0;
        var sanitizeAnsi = embeddedAnsi != EmbeddedAnsiMode.Passthrough;
        // Stray control characters are only cleaned up alongside actual escape sequences, so plain
        // multiline messages keep their \r\n untouched.
        var stripControls = sanitizeAnsi && AnsiSanitizer.ContainsAnsi(originalFormat);
        var ansi = new AnsiMarkupState();

        while (i < originalFormat.Length)
        {
            var c = originalFormat[i];

            if (c == '{')
            {
                if (i + 1 < originalFormat.Length && originalFormat[i + 1] == '{')
                {
                    ansi.BeforeAppend(builder);
                    builder.Append("{{");
                    i += 2;
                    continue;
                }

                var end = originalFormat.IndexOf('}', i + 1);
                if (end < 0)
                {
                    ansi.BeforeAppend(builder);
                    builder.Append(AnsiSanitizer.EscapeAndSanitize(originalFormat[i..], embeddedAnsi));
                    break;
                }

                var inside = originalFormat.AsSpan(i + 1, end - i - 1);
                var colonIdx = inside.IndexOf(':');
                string name;
                string? format = null;
                if (colonIdx >= 0)
                {
                    name = inside[..colonIdx].ToString();
                    format = inside[(colonIdx + 1)..].ToString();
                }
                else
                {
                    name = inside.ToString();
                }

                if (name.Length > 0 && name[0] == '@')
                {
                    name = name[1..];
                }

                var placeholder = FindPlaceholder(placeholders, name, ref nextPositional);
                var (rendered, unmaskedValue, masked) = PlaceholderFormatter.Render(placeholder, format, theme, masker, embeddedAnsi);
                ansi.BeforeAppend(builder);
                builder.Append(rendered);

                if (masked && unmaskedValue is not null && collectMaskValues is not null)
                {
                    collectMaskValues.Add(unmaskedValue);
                }

                i = end + 1;
                continue;
            }

            if (c == '}')
            {
                ansi.BeforeAppend(builder);
                if (i + 1 < originalFormat.Length && originalFormat[i + 1] == '}')
                {
                    builder.Append("}}");
                    i += 2;
                    continue;
                }
                builder.Append('}');
                i++;
                continue;
            }

            if (c == AnsiSanitizer.EscapeChar || c == AnsiSanitizer.CsiChar)
            {
                if (sanitizeAnsi)
                {
                    ansi.ConsumeSequence(originalFormat, ref i, embeddedAnsi == EmbeddedAnsiMode.Convert);
                }
                else
                {
                    builder.Append(c);
                    i++;
                }
                continue;
            }

            if (stripControls && char.IsControl(c) && c != '\n' && c != '\t')
            {
                i++;
                continue;
            }

            if (c == '[' || c == ']')
            {
                ansi.BeforeAppend(builder);
                if (!allowMarkupInTemplate)
                {
                    builder.Append(c).Append(c);
                }
                else
                {
                    builder.Append(c);
                }
                i++;
                continue;
            }

            ansi.BeforeAppend(builder);
            builder.Append(c);
            i++;
        }

        ansi.Flush(builder);
        return builder.ToString();
    }

    private static Placeholder FindPlaceholder(Placeholder[] placeholders, string name, ref int positionalIndex)
    {
        if (int.TryParse(name, out var index))
        {
            if (index >= 0 && index < placeholders.Length)
            {
                return placeholders[index];
            }
            return new Placeholder(name, null, null);
        }

        for (var i = 0; i < placeholders.Length; i++)
        {
            if (string.Equals(placeholders[i].Name, name, StringComparison.Ordinal))
            {
                return placeholders[i];
            }
        }

        if (positionalIndex < placeholders.Length)
        {
            return placeholders[positionalIndex++];
        }

        return new Placeholder(name, null, null);
    }
}
