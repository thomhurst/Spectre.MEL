namespace Spectre.MEL.Templates;

internal enum SegmentKind
{
    Literal,
    Timestamp,
    Level,
    Category,
    EventId,
    Message,
    Exception,
    NewLine,
    Scope,
    TraceId,
    SpanId,
    Custom,
}

internal readonly record struct TemplateSegment(SegmentKind Kind, string? Name, string? Format, string? Literal);
