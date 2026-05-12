using System.Text;
using Spectre.Console;

namespace Spectre.MEL.Rendering;

internal static class MarkupHelper
{
    public static string Escape(string text) => Markup.Escape(text);

    public static void AppendStyled(StringBuilder builder, string text, Style style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (IsPlain(style))
        {
            builder.Append(Markup.Escape(text));
            return;
        }

        builder.Append('[');
        builder.Append(style.ToMarkup());
        builder.Append(']');
        builder.Append(Markup.Escape(text));
        builder.Append("[/]");
    }

    public static bool IsPlain(Style style) =>
        style.Foreground == Color.Default && style.Background == Color.Default && style.Decoration == Decoration.None;
}
