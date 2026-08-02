using Spectre.Console;
using MEL.Spectre.Ci;

namespace MEL.Spectre.Provider;

internal sealed class SynchronousWriter : ILogEntryWriter
{
    private readonly LogEntryRenderer _entryRenderer;
    private readonly SequenceCompletionTracker _completionTracker = new();
    private readonly RenderGate _renderGate = new();
    private readonly TimeSpan _drainTimeout;
    private OnceFlag _droppedAfterDisposeWarning;
    private OnceFlag _drainTimeoutWarning;
    private int _disposeStarted;
    private int _disposed;

    public SynchronousWriter(IAnsiConsole console, ICiRenderer renderer, TimeSpan drainTimeout)
    {
        _entryRenderer = new LogEntryRenderer(console, renderer);
        _drainTimeout = drainTimeout;
    }

    public object SynchronizationLock { get; } = new();

    internal int PendingEntryCount => _completionTracker.PendingEntryCount;

    public void Enqueue(LogEntry entry)
    {
        var sequence = _completionTracker.Begin();
        var ownsSynchronizationLock = Monitor.IsEntered(SynchronizationLock);
        if (!ownsSynchronizationLock)
        {
            _renderGate.Enter();
        }
        try
        {
            lock (SynchronizationLock)
            {
                if (Volatile.Read(ref _disposed) != 0)
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
            if (!ownsSynchronizationLock)
            {
                _renderGate.Exit();
            }
            _completionTracker.Complete(sequence);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken) =>
        _completionTracker.FlushAsync(cancellationToken);

    public bool TryAcquireRenderGate(TimeSpan timeout, out IDisposable? gate) =>
        _renderGate.TryAcquire(timeout, out gate);

    public ValueTask<IDisposable?> TryAcquireRenderGateAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _renderGate.TryAcquireAsync(timeout, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        var gate = await _renderGate.TryAcquireAsync(_drainTimeout, CancellationToken.None).ConfigureAwait(false);
        if (gate is null)
        {
            Volatile.Write(ref _disposed, 1);
            if (_drainTimeoutWarning.TrySet())
            {
                LogWriterDiagnostics.Emit($"MEL.Spectre: synchronous drain timeout after {_drainTimeout}; open scopes may remain.");
            }
            return;
        }

        using (gate)
        {
            lock (SynchronizationLock)
            {
                Volatile.Write(ref _disposed, 1);
                _entryRenderer.CloseAllScopes();
            }
        }
    }
}
