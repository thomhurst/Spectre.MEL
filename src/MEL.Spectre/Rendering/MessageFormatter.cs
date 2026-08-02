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
        string plainText;
        try
        {
            plainText = rendered.IndexOfAny('[', ']') >= 0
                ? Markup.Remove(rendered)
                : rendered;
        }
        catch (InvalidOperationException)
        {
            // Raw message markup is validated by the renderer before any output. Leave malformed
            // markup unchanged so that validation can select the escaped fallback path.
            return rendered;
        }
        if (AnsiSanitizer.ContainsAnsi(plainText))
        {
            plainText = StripAnsiForMasking(plainText);
        }
        plainText = DropInvisibleControls(plainText);

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
        var rewrittenAnsi = RewriteMaskedAnsiSequences(rendered, ranges);
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
                if (rewrittenAnsi.TryGetValue(sequenceStart, out var replacement))
                {
                    builder.Append(replacement);
                }
                else
                {
                    builder.Append(rendered, sequenceStart, i - sequenceStart);
                }
                continue;
            }

            if (IsDroppedControl(rendered[i]))
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

            if (IsDroppedControl(rendered[i]))
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

    private static Dictionary<int, string> RewriteMaskedAnsiSequences(string rendered, List<SecretMasker.MaskRange> ranges)
    {
        var rewritten = new Dictionary<int, string>();
        List<int>? activeSequences = null;
        string? activeResetReplacement = null;
        var activeStart = 0;
        var visibleIndex = 0;
        var ansi = new AnsiMarkupState();

        for (var i = 0; i < rendered.Length;)
        {
            if (TryReadMarkupTag(rendered, i, out var tagEnd))
            {
                i = tagEnd;
                continue;
            }

            if (AnsiSanitizer.IsSequenceIntroducer(rendered[i]))
            {
                var sequenceStart = i;
                var wasActive = ansi.HasActiveStyle;
                ConsumeRenderedAnsiSequence(rendered, ref i, ref ansi, trackStyle: true);
                var isActive = ansi.HasActiveStyle;

                if (wasActive && isActive && ansi.LastSequenceReset)
                {
                    var priorIntersects = IntersectsMask(activeStart, visibleIndex, ranges);
                    RewriteAnsiSpan(activeSequences, priorIntersects, activeResetReplacement, rewritten);

                    activeStart = visibleIndex;
                    activeSequences = [sequenceStart];
                    activeResetReplacement = priorIntersects ? null : GetResetSequence(rendered, sequenceStart);
                }
                else if (!wasActive && isActive)
                {
                    activeStart = visibleIndex;
                    activeSequences = [sequenceStart];
                    activeResetReplacement = null;
                }
                else if (wasActive)
                {
                    activeSequences ??= [];
                    activeSequences.Add(sequenceStart);
                    if (!isActive)
                    {
                        RewriteAnsiSpan(activeSequences, IntersectsMask(activeStart, visibleIndex, ranges), activeResetReplacement, rewritten);
                        activeSequences = null;
                        activeResetReplacement = null;
                    }
                }
                else if (IsInsideOrAtRangeBoundary(visibleIndex, ranges))
                {
                    rewritten[sequenceStart] = string.Empty;
                }
                continue;
            }

            if (IsDroppedControl(rendered[i]))
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

        if (activeSequences is not null)
        {
            RewriteAnsiSpan(activeSequences, IntersectsMask(activeStart, visibleIndex, ranges), activeResetReplacement, rewritten);
        }

        return rewritten;
    }

    private static void RewriteAnsiSpan(List<int>? sequences, bool intersectsMask, string? resetReplacement, Dictionary<int, string> rewritten)
    {
        if (!intersectsMask || sequences is null)
        {
            return;
        }

        for (var i = 0; i < sequences.Count; i++)
        {
            rewritten[sequences[i]] = string.Empty;
        }
        if (resetReplacement is not null)
        {
            rewritten[sequences[0]] = resetReplacement;
        }
    }

    private static string GetResetSequence(string rendered, int start) =>
        rendered[start] == AnsiSanitizer.CsiChar
            ? "\x9b0m"
            : start + 2 < rendered.Length && rendered[start + 1] == '[' && rendered[start + 2] == '['
                ? "\x1b[[0m"
                : "\x1b[0m";

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

    private static bool IsDroppedControl(char value) =>
        char.IsControl(value) && value != '\r' && value != '\n' && value != '\t';

    private static string DropInvisibleControls(string text)
    {
        var firstControl = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (IsDroppedControl(text[i]))
            {
                firstControl = i;
                break;
            }
        }

        if (firstControl < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, firstControl);
        for (var i = firstControl + 1; i < text.Length; i++)
        {
            if (!IsDroppedControl(text[i]))
            {
                builder.Append(text[i]);
            }
        }
        return builder.ToString();
    }

    private static string StripAnsiForMasking(string text)
    {
        var builder = new StringBuilder(text.Length);
        var ansi = new AnsiMarkupState();
        AnsiSanitizer.AppendSanitized(
            builder,
            text,
            0,
            ref ansi,
            convert: false,
            escapeMarkup: false,
            stripControls: false);
        return builder.ToString();
    }

    private static void ConsumeRenderedAnsiSequence(string text, ref int index, ref AnsiMarkupState ansi, bool trackStyle = false)
    {
        if (text[index] == AnsiSanitizer.EscapeChar &&
            index + 2 < text.Length &&
            text[index + 1] == '[' &&
            text[index + 2] == '[')
        {
            var parameterStart = index + 3;
            var current = parameterStart;
            while (current < text.Length && text[current] >= '\x30' && text[current] <= '\x3f')
            {
                current++;
            }
            var parameterEnd = current;
            while (current < text.Length && text[current] >= '\x20' && text[current] <= '\x2f')
            {
                current++;
            }
            if (trackStyle && current < text.Length && text[current] == 'm' && parameterEnd == current)
            {
                ansi.ApplySgrParameters(text.AsSpan(parameterStart, parameterEnd - parameterStart));
            }
            index = current < text.Length && text[current] >= '\x40' && text[current] <= '\x7e'
                ? current + 1
                : current;
            return;
        }

        ansi.ConsumeSequence(text, ref index, convert: trackStyle);
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

    private static bool IsInsideOrAtRangeBoundary(int position, List<SecretMasker.MaskRange> ranges)
    {
        for (var i = 0; i < ranges.Count && ranges[i].Start <= position; i++)
        {
            if (position <= ranges[i].End)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IntersectsMask(int start, int end, List<SecretMasker.MaskRange> ranges)
    {
        for (var i = 0; i < ranges.Count && ranges[i].Start < end; i++)
        {
            if (ranges[i].End > start)
            {
                return true;
            }
        }
        return start == end && IsInsideOrAtRangeBoundary(start, ranges);
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
