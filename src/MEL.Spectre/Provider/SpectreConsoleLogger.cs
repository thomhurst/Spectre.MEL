using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Provider;

internal sealed class SpectreConsoleLogger : ILogger
{
    private readonly string _category;
    private readonly BackgroundWriter _writer;
    private readonly Func<IExternalScopeProvider?> _scopeProviderAccessor;
    private readonly bool _includeScopes;
    private readonly bool _includeActivity;

    public SpectreConsoleLogger(
        string category,
        BackgroundWriter writer,
        Func<IExternalScopeProvider?> scopeProviderAccessor,
        bool includeScopes,
        bool includeActivity)
    {
        _category = category;
        _writer = writer;
        _scopeProviderAccessor = scopeProviderAccessor;
        _includeScopes = includeScopes;
        _includeActivity = includeActivity;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProviderAccessor()?.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var (originalFormat, placeholders) = StateReader.Extract(state);
        var scopes = _includeScopes ? CaptureScopes() : [];
        var activity = _includeActivity ? Activity.Current : null;

        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = logLevel,
            Category = _category,
            EventId = eventId,
            Message = message,
            OriginalFormat = originalFormat,
            Exception = exception,
            Placeholders = placeholders,
            Scopes = scopes,
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
        };

        _writer.Enqueue(entry);
    }

    private ScopeFrame[] CaptureScopes()
    {
        var provider = _scopeProviderAccessor();
        if (provider is null)
        {
            return [];
        }

        var list = new List<ScopeFrame>(2);
        provider.ForEachScope(static (state, ctx) =>
        {
            var label = state?.ToString() ?? string.Empty;
            var props = state as IReadOnlyList<KeyValuePair<string, object?>>;
            var id = state is null ? 0 : RuntimeHelpers.GetHashCode(state);
            ctx.Add(new ScopeFrame(id, label, props));
        }, list);

        return list.ToArray();
    }
}
