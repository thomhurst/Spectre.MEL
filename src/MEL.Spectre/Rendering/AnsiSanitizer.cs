using System.Text;
using Spectre.Console;

namespace MEL.Spectre.Rendering;

/// <summary>
/// Handles raw ANSI/VT control sequences embedded in message content (typically child-process output
/// relayed through the logger). SGR styling sequences can be translated into balanced Spectre markup so
/// embedded colors nest inside the logger's own styling instead of resetting it mid-line; every other
/// control sequence is dropped.
/// </summary>
internal static class AnsiSanitizer
{
    internal const char EscapeChar = '\x1b';
    internal const char CsiChar = '\x9b'; // single-character (8-bit) CSI introducer

    public static bool ContainsAnsi(string text) => text.AsSpan().IndexOfAny(EscapeChar, CsiChar) >= 0;

    /// <summary>
    /// Markup-escapes <paramref name="text"/> while removing embedded control sequences, or — in
    /// <see cref="EmbeddedAnsiMode.Convert"/> — translating SGR sequences into balanced markup tags.
    /// With <paramref name="escapeMarkup"/> false, bracket characters pass through untouched for
    /// consumers that allow markup in message templates.
    /// </summary>
    public static string EscapeAndSanitize(string text, EmbeddedAnsiMode mode, bool escapeMarkup = true)
    {
        if (mode == EmbeddedAnsiMode.Passthrough || !ContainsAnsi(text))
        {
            return escapeMarkup ? Markup.Escape(text) : text;
        }

        var builder = new StringBuilder(text.Length + 16);
        var state = new AnsiMarkupState();
        var convert = mode == EmbeddedAnsiMode.Convert;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == EscapeChar || c == CsiChar)
            {
                state.ConsumeSequence(text, ref i, convert);
                continue;
            }

            if (char.IsControl(c) && c != '\n' && c != '\t')
            {
                i++;
                continue;
            }

            state.BeforeAppend(builder);
            if (escapeMarkup && (c == '[' || c == ']'))
            {
                builder.Append(c).Append(c);
            }
            else
            {
                builder.Append(c);
            }
            i++;
        }

        state.Flush(builder);
        return builder.ToString();
    }
}

/// <summary>
/// Incremental ANSI-to-markup translation state. Callers feed control sequences via
/// <see cref="ConsumeSequence"/>, call <see cref="BeforeAppend"/> before appending any visible content,
/// and <see cref="Flush"/> once at the end so every opened markup tag is closed.
/// </summary>
internal struct AnsiMarkupState
{
    private Color? _foreground;
    private Color? _background;
    private Decoration _decoration;
    private bool _tagOpen;
    private bool _stylePending;

    /// <summary>
    /// Consumes the control sequence starting at <paramref name="i"/> (an ESC or 8-bit CSI character) and
    /// advances <paramref name="i"/> past it. SGR sequences update the pending style when
    /// <paramref name="convert"/> is true; everything else is discarded.
    /// </summary>
    public void ConsumeSequence(string text, ref int i, bool convert)
    {
        if (text[i] == AnsiSanitizer.CsiChar)
        {
            ConsumeControlSequence(text, ref i, i + 1, convert);
            return;
        }

        if (i + 1 >= text.Length)
        {
            i++; // dangling ESC at end of input
            return;
        }

        switch (text[i + 1])
        {
            case '[':
                ConsumeControlSequence(text, ref i, i + 2, convert);
                break;
            case ']': // OSC
            case 'P': // DCS
            case 'X': // SOS
            case '^': // PM
            case '_': // APC
                i = SkipStringSequence(text, i + 2);
                break;
            default:
                // ESC + optional intermediates (0x20-0x2F) + one final byte, e.g. ESC 7 or ESC ( B.
                var j = i + 1;
                while (j < text.Length && text[j] >= '\x20' && text[j] <= '\x2f')
                {
                    j++;
                }
                i = j < text.Length ? j + 1 : j;
                break;
        }
    }

    /// <summary>Emits the markup tag for a pending style change ahead of visible content.</summary>
    public void BeforeAppend(StringBuilder builder)
    {
        if (!_stylePending)
        {
            return;
        }
        _stylePending = false;

        if (_tagOpen)
        {
            builder.Append("[/]");
            _tagOpen = false;
        }

        if (_foreground is null && _background is null && _decoration == Decoration.None)
        {
            return;
        }

        builder.Append('[');
        builder.Append(new Style(_foreground, _background, _decoration).ToMarkup());
        builder.Append(']');
        _tagOpen = true;
    }

    /// <summary>Closes any markup tag left open so the produced markup is always balanced.</summary>
    public void Flush(StringBuilder builder)
    {
        if (_tagOpen)
        {
            builder.Append("[/]");
            _tagOpen = false;
        }
    }

    private void ConsumeControlSequence(string text, ref int i, int start, bool convert)
    {
        var j = start;
        while (j < text.Length && text[j] >= '\x30' && text[j] <= '\x3f')
        {
            j++;
        }
        var parameterEnd = j;
        while (j < text.Length && text[j] >= '\x20' && text[j] <= '\x2f')
        {
            j++;
        }
        if (j >= text.Length)
        {
            i = j; // truncated sequence — drop the remainder
            return;
        }

        var final = text[j];
        i = j + 1;
        if (convert && final == 'm' && parameterEnd == j)
        {
            ApplySgr(text.AsSpan(start, parameterEnd - start));
        }
    }

    private void ApplySgr(ReadOnlySpan<char> parameters)
    {
        _stylePending = true;

        if (parameters.IsEmpty)
        {
            Reset();
            return;
        }

        // SGR parameters are small integers separated by ';' (or ':' in some emitters' extended colors).
        Span<int> values = stackalloc int[32];
        var count = 0;
        var current = 0;
        foreach (var c in parameters)
        {
            if (c >= '0' && c <= '9')
            {
                current = Math.Min(current * 10 + (c - '0'), 99_999);
            }
            else if (c == ';' || c == ':')
            {
                if (count == values.Length)
                {
                    return;
                }
                values[count++] = current;
                current = 0;
            }
            else
            {
                return; // private-mode or otherwise non-SGR parameter bytes — not a styling sequence
            }
        }
        if (count == values.Length)
        {
            return;
        }
        values[count++] = current;

        Apply(values[..count]);
    }

    private void Apply(ReadOnlySpan<int> values)
    {
        for (var k = 0; k < values.Length; k++)
        {
            switch (values[k])
            {
                case 0: Reset(); break;
                case 1: _decoration |= Decoration.Bold; break;
                case 2: _decoration |= Decoration.Dim; break;
                case 3: _decoration |= Decoration.Italic; break;
                case 4: _decoration |= Decoration.Underline; break;
                case 5: _decoration |= Decoration.SlowBlink; break;
                case 6: _decoration |= Decoration.RapidBlink; break;
                case 7: _decoration |= Decoration.Invert; break;
                case 8: _decoration |= Decoration.Conceal; break;
                case 9: _decoration |= Decoration.Strikethrough; break;
                case 21 or 22: _decoration &= ~(Decoration.Bold | Decoration.Dim); break;
                case 23: _decoration &= ~Decoration.Italic; break;
                case 24: _decoration &= ~Decoration.Underline; break;
                case 25: _decoration &= ~(Decoration.SlowBlink | Decoration.RapidBlink); break;
                case 27: _decoration &= ~Decoration.Invert; break;
                case 28: _decoration &= ~Decoration.Conceal; break;
                case 29: _decoration &= ~Decoration.Strikethrough; break;
                case >= 30 and <= 37: _foreground = Color.FromInt32(values[k] - 30); break;
                case 38: k = ReadExtendedColor(values, k, ref _foreground); break;
                case 39: _foreground = null; break;
                case >= 40 and <= 47: _background = Color.FromInt32(values[k] - 40); break;
                case 48: k = ReadExtendedColor(values, k, ref _background); break;
                case 49: _background = null; break;
                case >= 90 and <= 97: _foreground = Color.FromInt32(values[k] - 90 + 8); break;
                case >= 100 and <= 107: _background = Color.FromInt32(values[k] - 100 + 8); break;
            }
        }
    }

    private static int ReadExtendedColor(ReadOnlySpan<int> values, int k, ref Color? target)
    {
        if (k + 1 >= values.Length)
        {
            return values.Length;
        }

        switch (values[k + 1])
        {
            case 5 when k + 2 < values.Length:
                if (values[k + 2] <= 255)
                {
                    target = Color.FromInt32(values[k + 2]);
                }
                return k + 2;
            case 2 when k + 4 < values.Length:
                target = new Color(ToByte(values[k + 2]), ToByte(values[k + 3]), ToByte(values[k + 4]));
                return k + 4;
            default:
                return values.Length; // malformed extended color — skip the remaining parameters
        }
    }

    private static byte ToByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private void Reset()
    {
        _foreground = null;
        _background = null;
        _decoration = Decoration.None;
    }

    private static int SkipStringSequence(string text, int start)
    {
        // OSC/DCS/SOS/PM/APC payloads run until BEL or ST (ESC \ or 8-bit 0x9C).
        var j = start;
        while (j < text.Length)
        {
            var c = text[j];
            if (c == '\x07' || c == '\x9c')
            {
                return j + 1;
            }
            if (c == AnsiSanitizer.EscapeChar)
            {
                return j + 1 < text.Length && text[j + 1] == '\\' ? j + 2 : j;
            }
            j++;
        }
        return j;
    }
}
