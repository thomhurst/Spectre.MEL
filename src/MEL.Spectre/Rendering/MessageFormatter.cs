using System.Text;
using Spectre.Console;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Theme;

namespace MEL.Spectre.Rendering;

internal static class MessageFormatter
{
    public static string Render(string? originalFormat, string fallback, Placeholder[] placeholders, SpectreTheme theme, SecretMasker masker, List<string>? collectMaskValues = null, bool allowMarkupInTemplate = false, EmbeddedAnsiMode embeddedAnsi = EmbeddedAnsiMode.Convert, bool maskValuePatternsInMessageText = true)
    {
        if (string.IsNullOrEmpty(originalFormat))
        {
            var renderedFallback = AnsiSanitizer.EscapeAndSanitize(fallback, embeddedAnsi, escapeMarkup: !allowMarkupInTemplate);
            return maskValuePatternsInMessageText && masker.HasValuePatterns
                ? MaskMessageText(renderedFallback, masker, collectMaskValues)
                : renderedFallback;
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
                    builder.Append('{');
                    i += 2;
                    continue;
                }

                var end = originalFormat.IndexOf('}', i + 1);
                if (end < 0)
                {
                    if (sanitizeAnsi)
                    {
                        // Continue with the shared state so a reset inside the tail still closes a
                        // style opened before the unmatched brace.
                        AnsiSanitizer.AppendSanitized(builder, originalFormat, i, ref ansi, embeddedAnsi == EmbeddedAnsiMode.Convert, escapeMarkup: !allowMarkupInTemplate, stripControls);
                    }
                    else
                    {
                        var tail = originalFormat[i..];
                        builder.Append(allowMarkupInTemplate ? tail : Markup.Escape(tail));
                    }
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
                    builder.Append('}');
                    i += 2;
                    continue;
                }
                builder.Append('}');
                i++;
                continue;
            }

            if (AnsiSanitizer.IsSequenceIntroducer(c))
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
        var renderedMessage = builder.ToString();
        return maskValuePatternsInMessageText && masker.HasValuePatterns
            ? MaskMessageText(renderedMessage, masker, collectMaskValues)
            : renderedMessage;
    }

    private static string MaskMessageText(string rendered, SecretMasker masker, List<string>? collectMaskValues)
    {
        var plainText = rendered.IndexOfAny('[', ']') >= 0
            ? Markup.Remove(rendered)
            : rendered;
        if (AnsiSanitizer.ContainsAnsi(plainText))
        {
            plainText = AnsiSanitizer.EscapeAndSanitize(plainText, EmbeddedAnsiMode.Strip, escapeMarkup: false);
        }

        var ranges = masker.GetValuePatternMaskRanges(plainText, collectMaskValues);
        if (ranges.Count == 0)
        {
            return rendered;
        }

        return MaskRenderedText(rendered, ranges);
    }

    private static string MaskRenderedText(string rendered, List<SecretMasker.MaskRange> ranges)
    {
        var droppedTags = FindFullyMaskedMarkupTags(rendered, ranges);
        var builder = new StringBuilder(rendered.Length);
        var visibleIndex = 0;
        var rangeIndex = 0;
        var ansi = new AnsiMarkupState();

        for (var i = 0; i < rendered.Length;)
        {
            if (TryReadMarkupTag(rendered, i, out var tagEnd))
            {
                if (!droppedTags.Contains(i))
                {
                    builder.Append(rendered, i, tagEnd - i);
                }
                i = tagEnd;
                continue;
            }

            if (AnsiSanitizer.IsSequenceIntroducer(rendered[i]))
            {
                var sequenceStart = i;
                ConsumeRenderedAnsiSequence(rendered, ref i, ref ansi);
                if (!IsStrictlyInsideRange(visibleIndex, ranges, rangeIndex))
                {
                    builder.Append(rendered, sequenceStart, i - sequenceStart);
                }
                continue;
            }

            if (char.IsControl(rendered[i]) && rendered[i] != '\n' && rendered[i] != '\t')
            {
                i++;
                continue;
            }

            var rawLength = i + 1 < rendered.Length &&
                ((rendered[i] == '[' && rendered[i + 1] == '[') ||
                 (rendered[i] == ']' && rendered[i + 1] == ']'))
                ? 2
                : 1;

            while (rangeIndex < ranges.Count && visibleIndex >= ranges[rangeIndex].End)
            {
                rangeIndex++;
            }

            if (rangeIndex < ranges.Count && visibleIndex == ranges[rangeIndex].Start)
            {
                builder.Append("***");
            }

            if (rangeIndex >= ranges.Count || visibleIndex < ranges[rangeIndex].Start)
            {
                builder.Append(rendered, i, rawLength);
            }

            visibleIndex++;
            i += rawLength;
        }

        if (rangeIndex < ranges.Count && visibleIndex == ranges[rangeIndex].Start)
        {
            builder.Append("***");
        }

        return builder.ToString();
    }

    private static HashSet<int> FindFullyMaskedMarkupTags(string rendered, List<SecretMasker.MaskRange> ranges)
    {
        var dropped = new HashSet<int>();
        var openTags = new Stack<(int RawIndex, int VisibleIndex)>();
        var visibleIndex = 0;
        var ansi = new AnsiMarkupState();

        for (var i = 0; i < rendered.Length;)
        {
            if (TryReadMarkupTag(rendered, i, out var tagEnd))
            {
                if (rendered.AsSpan(i).StartsWith("[/]", StringComparison.Ordinal))
                {
                    if (openTags.TryPop(out var openTag) && IsFullyCovered(openTag.VisibleIndex, visibleIndex, ranges))
                    {
                        dropped.Add(openTag.RawIndex);
                        dropped.Add(i);
                    }
                }
                else
                {
                    openTags.Push((i, visibleIndex));
                }
                i = tagEnd;
                continue;
            }

            if (AnsiSanitizer.IsSequenceIntroducer(rendered[i]))
            {
                ConsumeRenderedAnsiSequence(rendered, ref i, ref ansi);
                continue;
            }

            if (char.IsControl(rendered[i]) && rendered[i] != '\n' && rendered[i] != '\t')
            {
                i++;
                continue;
            }

            i += i + 1 < rendered.Length &&
                ((rendered[i] == '[' && rendered[i + 1] == '[') ||
                 (rendered[i] == ']' && rendered[i + 1] == ']'))
                ? 2
                : 1;
            visibleIndex++;
        }

        return dropped;
    }

    private static bool TryReadMarkupTag(string text, int start, out int end)
    {
        end = start;
        if (text[start] != '[' || (start + 1 < text.Length && text[start + 1] == '['))
        {
            return false;
        }

        var closingBracket = text.IndexOf(']', start + 1);
        if (closingBracket < 0)
        {
            return false;
        }

        end = closingBracket + 1;
        return true;
    }

    private static void ConsumeRenderedAnsiSequence(string text, ref int index, ref AnsiMarkupState ansi)
    {
        if (text[index] == AnsiSanitizer.EscapeChar &&
            index + 2 < text.Length &&
            text[index + 1] == '[' &&
            text[index + 2] == '[')
        {
            var current = index + 3;
            while (current < text.Length && text[current] >= '\x30' && text[current] <= '\x3f')
            {
                current++;
            }
            while (current < text.Length && text[current] >= '\x20' && text[current] <= '\x2f')
            {
                current++;
            }
            index = current < text.Length && text[current] >= '\x40' && text[current] <= '\x7e'
                ? current + 1
                : current;
            return;
        }

        ansi.ConsumeSequence(text, ref index, convert: false);
    }

    private static bool IsFullyCovered(int start, int end, List<SecretMasker.MaskRange> ranges)
    {
        for (var i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Start <= start && ranges[i].End >= end && start < end)
            {
                return true;
            }
            if (ranges[i].Start > start)
            {
                return false;
            }
        }
        return false;
    }

    private static bool IsStrictlyInsideRange(int position, List<SecretMasker.MaskRange> ranges, int startIndex)
    {
        for (var i = startIndex; i < ranges.Count && ranges[i].Start < position; i++)
        {
            if (position < ranges[i].End)
            {
                return true;
            }
        }
        return false;
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
