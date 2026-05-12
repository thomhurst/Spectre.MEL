using Microsoft.Extensions.Logging;
using Spectre.MEL.Scopes;

namespace Spectre.MEL.Provider;

internal sealed record LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public required string Category { get; init; }
    public EventId EventId { get; init; }
    public required string Message { get; init; }
    public string? OriginalFormat { get; init; }
    public Exception? Exception { get; init; }
    public Placeholder[] Placeholders { get; init; } = [];
    public ScopeFrame[] Scopes { get; init; } = [];
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
}
