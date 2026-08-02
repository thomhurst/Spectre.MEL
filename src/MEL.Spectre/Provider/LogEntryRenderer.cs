using Spectre.Console;
using MEL.Spectre.Ci;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Provider;

internal sealed class LogEntryRenderer
{
    private readonly IAnsiConsole _console;
    private readonly ICiRenderer _renderer;
    private readonly Stack<ScopeFrame> _activeScopes = new();
    private OnceFlag _scopesClosed;

    public LogEntryRenderer(IAnsiConsole console, ICiRenderer renderer)
    {
        _console = console;
        _renderer = renderer;
    }

    public void Render(LogEntry entry)
    {
        try
        {
            if (!_scopesClosed.IsSet)
            {
                ReconcileScopes(entry.Scopes);
            }
            _renderer.RenderEntry(_console, entry, _activeScopes.Count);
        }
        catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
        {
            try
            {
                _renderer.RenderEntryFallback(_console, entry, _activeScopes.Count);
                LogWriterDiagnostics.Emit($"MEL.Spectre: render fault recovered with escaped plain text: {ex}");
            }
            catch (Exception fallbackEx) when (!FatalExceptions.IsFatal(fallbackEx))
            {
                LogWriterDiagnostics.Emit($"MEL.Spectre: render fault: {ex}{Environment.NewLine}MEL.Spectre: fallback render fault: {fallbackEx}");
            }
        }
    }

    public void CloseAllScopes()
    {
        if (!_scopesClosed.TrySet())
        {
            return;
        }

        try
        {
            while (_activeScopes.Count > 0)
            {
                var frame = _activeScopes.Pop();
                _renderer.CloseScope(_console, frame, _activeScopes.Count);
            }
        }
        catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
        {
            LogWriterDiagnostics.Emit($"MEL.Spectre: scope close fault: {ex}");
        }
    }

    private void ReconcileScopes(ScopeFrame[] incoming)
    {
        var current = _activeScopes.Reverse().ToArray();
        var commonPrefix = 0;
        var max = Math.Min(current.Length, incoming.Length);
        while (commonPrefix < max && current[commonPrefix].Id == incoming[commonPrefix].Id)
        {
            commonPrefix++;
        }

        while (_activeScopes.Count > commonPrefix)
        {
            var frame = _activeScopes.Pop();
            _renderer.CloseScope(_console, frame, _activeScopes.Count);
        }

        for (var i = commonPrefix; i < incoming.Length; i++)
        {
            _renderer.OpenScope(_console, incoming[i], _activeScopes.Count);
            _activeScopes.Push(incoming[i]);
        }
    }
}
