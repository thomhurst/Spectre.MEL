using Spectre.Console;
using MEL.Spectre.Ci;

namespace MEL.Spectre.Provider;

internal sealed class SynchronousWriter : ILogEntryWriter
{
    private readonly LogEntryRenderer _entryRenderer;
    private OnceFlag _droppedAfterDisposeWarning;
    private bool _disposed;

    public SynchronousWriter(IAnsiConsole console, ICiRenderer renderer)
    {
        _entryRenderer = new LogEntryRenderer(console, renderer);
    }

    public object SynchronizationLock { get; } = new();

    public void Enqueue(LogEntry entry)
    {
        lock (SynchronizationLock)
        {
            if (_disposed)
            {
                if (_droppedAfterDisposeWarning.TrySet())
                {
                    LogWriterDiagnostics.Emit("MEL.Spectre: log entry dropped after provider disposal.");
                }
                return;
            }

            _entryRenderer.Render(entry);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        lock (SynchronizationLock)
        {
            return Task.CompletedTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (SynchronizationLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                _entryRenderer.CloseAllScopes();
            }
        }

        return ValueTask.CompletedTask;
    }
}
