namespace Spectre.MEL.Templates;

internal static class TemplateParser
{
    public static TemplateSegment[] Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var segments = new List<TemplateSegment>();
        var literal = new System.Text.StringBuilder();
        var i = 0;

        while (i < template.Length)
        {
            var c = template[i];

            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }

                if (literal.Length > 0)
                {
                    segments.Add(new TemplateSegment(SegmentKind.Literal, null, null, literal.ToString()));
                    literal.Clear();
                }

                var end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    throw new FormatException($"Unterminated token starting at position {i} in template: '{template}'.");
                }

                var inside = template.AsSpan(i + 1, end - i - 1);
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

                segments.Add(new TemplateSegment(MapKind(name), name, format, null));
                i = end + 1;
                continue;
            }

            if (c == '}')
            {
                if (i + 1 < template.Length && template[i + 1] == '}')
                {
                    literal.Append('}');
                    i += 2;
                    continue;
                }

                throw new FormatException($"Unmatched '}}' at position {i} in template: '{template}'.");
            }

            literal.Append(c);
            i++;
        }

        if (literal.Length > 0)
        {
            segments.Add(new TemplateSegment(SegmentKind.Literal, null, null, literal.ToString()));
        }

        return segments.ToArray();
    }

    private static SegmentKind MapKind(string name) => name switch
    {
        "Timestamp" => SegmentKind.Timestamp,
        "Level" => SegmentKind.Level,
        "Category" => SegmentKind.Category,
        "EventId" => SegmentKind.EventId,
        "Message" => SegmentKind.Message,
        "Exception" => SegmentKind.Exception,
        "NewLine" => SegmentKind.NewLine,
        "Scope" => SegmentKind.Scope,
        "TraceId" => SegmentKind.TraceId,
        "SpanId" => SegmentKind.SpanId,
        _ => SegmentKind.Custom,
    };
}
