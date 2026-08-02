using Spectre.Console;
using MEL.Spectre.Ci;

namespace MEL.Spectre.Provider;

internal sealed class SynchronousWriter : ILogEntryWriter
{
    private readonly LogEntryRenderer _entryRenderer;
    private readonly SequenceCompletionTracker _completionTracker = new();
    private readonly RenderGate _renderGate = new();
    private OnceFlag _droppedAfterDisposeWarning;
    private bool _disposed;

    public SynchronousWriter(IAnsiConsole console, ICiRenderer renderer)
    {
        _entryRenderer = new LogEntryRenderer(console, renderer);
    }

    public object SynchronizationLock { get; } = new();

    internal int PendingEntryCount => _completionTracker.PendingEntryCount;

    public void Enqueue(LogEntry entry)
    {
        var sequence = _completionTracker.Begin();
        _renderGate.Enter();
        try
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
        finally
        {
            _renderGate.Exit();
            _completionTracker.Complete(sequence);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _completionTracker.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _renderGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        _renderGate.Exit();
    }

    public bool TryAcquireRenderGate(TimeSpan timeout, out IDisposable? gate) =>
        _renderGate.TryAcquire(timeout, out gate);

    public ValueTask<IDisposable?> TryAcquireRenderGateAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _renderGate.TryAcquireAsync(timeout, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _renderGate.Enter();
        try
        {
            lock (SynchronizationLock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _entryRenderer.CloseAllScopes();
                }
            }
        }
        finally
        {
            _renderGate.Exit();
        }

        return ValueTask.CompletedTask;
    }
}
