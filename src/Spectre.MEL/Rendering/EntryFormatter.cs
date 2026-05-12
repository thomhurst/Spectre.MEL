using System.Text;
using Microsoft.Extensions.Logging;
using Spectre.MEL.Masking;
using Spectre.MEL.Provider;
using Spectre.MEL.Templates;
using Spectre.MEL.Theme;

namespace Spectre.MEL.Rendering;

internal sealed class EntryFormatter
{
    private readonly OutputTemplate _template;
    private readonly SpectreTheme _theme;
    private readonly SecretMasker _masker;

    public EntryFormatter(OutputTemplate template, SpectreTheme theme, SecretMasker masker)
    {
        _template = template;
        _theme = theme;
        _masker = masker;
    }

    public string Format(LogEntry entry, List<string>? maskValueSink = null)
    {
        var builder = new StringBuilder(256);
        foreach (var segment in _template.Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Literal:
                    builder.Append(MarkupHelper.Escape(segment.Literal ?? string.Empty));
                    break;
                case SegmentKind.Timestamp:
                    MarkupHelper.AppendStyled(builder, FormatTimestamp(entry.Timestamp, segment.Format), _theme.TimestampStyle);
                    break;
                case SegmentKind.Level:
                    MarkupHelper.AppendStyled(builder, FormatLevel(entry.Level, segment.Format), _theme.ForLevel(entry.Level));
                    break;
                case SegmentKind.Category:
                    MarkupHelper.AppendStyled(builder, entry.Category, _theme.CategoryStyle);
                    break;
                case SegmentKind.EventId:
                    if (entry.EventId.Id != 0 || !string.IsNullOrEmpty(entry.EventId.Name))
                    {
                        MarkupHelper.AppendStyled(builder, FormatEventId(entry.EventId), _theme.EventIdStyle);
                    }
                    break;
                case SegmentKind.Message:
                    builder.Append(MessageFormatter.Render(entry.OriginalFormat, entry.Message, entry.Placeholders, _theme, _masker, maskValueSink));
                    break;
                case SegmentKind.NewLine:
                    builder.AppendLine();
                    break;
                case SegmentKind.Scope:
                    if (entry.Scopes.Length > 0)
                    {
                        var scopeLabel = new StringBuilder(32);
                        for (var i = 0; i < entry.Scopes.Length; i++)
                        {
                            if (i > 0) scopeLabel.Append(" / ");
                            scopeLabel.Append(entry.Scopes[i].Label);
                        }
                        MarkupHelper.AppendStyled(builder, scopeLabel.ToString(), _theme.ScopeStyle);
                    }
                    break;
                case SegmentKind.TraceId:
                    if (!string.IsNullOrEmpty(entry.TraceId))
                    {
                        MarkupHelper.AppendStyled(builder, entry.TraceId, _theme.EventIdStyle);
                    }
                    break;
                case SegmentKind.SpanId:
                    if (!string.IsNullOrEmpty(entry.SpanId))
                    {
                        MarkupHelper.AppendStyled(builder, entry.SpanId, _theme.EventIdStyle);
                    }
                    break;
                case SegmentKind.Custom:
                    var ph = FindCustom(entry.Placeholders, segment.Name!);
                    if (ph.HasValue)
                    {
                        var (rendered, _, _) = PlaceholderFormatter.Render(ph.Value, segment.Format, _theme, _masker);
                        builder.Append(rendered);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private static Placeholder? FindCustom(Placeholder[] placeholders, string name)
    {
        for (var i = 0; i < placeholders.Length; i++)
        {
            if (string.Equals(placeholders[i].Name, name, StringComparison.Ordinal))
            {
                return placeholders[i];
            }
        }
        return null;
    }

    private static string FormatTimestamp(DateTimeOffset ts, string? format) =>
        ts.LocalDateTime.ToString(string.IsNullOrEmpty(format) ? "HH:mm:ss" : format, System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatLevel(LogLevel level, string? format)
    {
        var name = level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => "NONE",
        };

        if (string.IsNullOrEmpty(format))
        {
            return name;
        }

        var prefix = format[0];
        if (prefix == 'l' || prefix == 'L')
        {
            name = name.ToLowerInvariant();
        }
        else if (prefix != 'u' && prefix != 'U')
        {
            return name;
        }

        if (format.Length > 1 && int.TryParse(format.AsSpan(1), out var width))
        {
            if (name.Length > width)
            {
                name = name[..width];
            }
            else
            {
                name = name.PadRight(width);
            }
        }

        return name;
    }

    private static string FormatEventId(EventId id)
    {
        if (!string.IsNullOrEmpty(id.Name))
        {
            return $"#{id.Id} {id.Name}";
        }
        return $"#{id.Id}";
    }
}
