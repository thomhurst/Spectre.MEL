using System.Threading.Channels;
using Spectre.Console;
using MEL.Spectre.Ci;

namespace MEL.Spectre.Provider;

internal sealed class BackgroundWriter : ILogEntryWriter
{
    private readonly Channel<QueuedEntry> _channel;
    private readonly LogEntryRenderer _entryRenderer;
    private readonly BackpressureMode _backpressureMode;
    private readonly TimeSpan _drainTimeout;
    private readonly TimeSpan _enqueueWaitTimeout;
    private readonly Task _consumerTask;
    private readonly SequenceCompletionTracker _completionTracker = new();
    private long _droppedAfterDispose;
    private long _droppedBackpressure;
    private long _droppedChannelFault;
    private OnceFlag _droppedAfterDisposeWarning;
    private OnceFlag _droppedBackpressureWarning;
    private OnceFlag _droppedChannelFaultWarning;
    private OnceFlag _drainTimeoutWarning;

    public BackgroundWriter(
        IAnsiConsole console,
        ICiRenderer renderer,
        int capacity,
        BackpressureMode backpressureMode,
        TimeSpan drainTimeout,
        TimeSpan enqueueWaitTimeout)
    {
        _entryRenderer = new LogEntryRenderer(console, renderer);
        _backpressureMode = backpressureMode;
        _drainTimeout = drainTimeout;
        _enqueueWaitTimeout = enqueueWaitTimeout;

        var fullMode = backpressureMode switch
        {
            BackpressureMode.DropNewest => BoundedChannelFullMode.DropWrite,
            BackpressureMode.DropOldest => BoundedChannelFullMode.DropOldest,
            _ => BoundedChannelFullMode.Wait,
        };

        _channel = Channel.CreateBounded<QueuedEntry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = fullMode,
        }, OnItemDropped);

        _consumerTask = Task.Run(ConsumeAsync);
    }

    public object SynchronizationLock { get; } = new();

    public long DroppedAfterDisposeCount => Interlocked.Read(ref _droppedAfterDispose);

    public long DroppedBackpressureCount => Interlocked.Read(ref _droppedBackpressure);

    public long DroppedChannelFaultCount => Interlocked.Read(ref _droppedChannelFault);

    internal int PendingCompletionRangeCount => _completionTracker.PendingCompletionRangeCount;

    internal int PendingFlushWaiterCount => _completionTracker.PendingFlushWaiterCount;

    public void Enqueue(LogEntry entry)
    {
        var queued = new QueuedEntry(entry, _completionTracker.Begin());
        if (_channel.Writer.TryWrite(queued))
        {
            return;
        }

        if (_channel.Reader.Completion.IsCompleted)
        {
            RecordDropAfterDispose();
            _completionTracker.Complete(queued.Sequence);
            return;
        }

        if (_backpressureMode != BackpressureMode.Wait)
        {
            RecordBackpressureDrop();
            _completionTracker.Complete(queued.Sequence);
            return;
        }

        WaitToWrite(queued);
    }

    public Task FlushAsync(CancellationToken cancellationToken) => _completionTracker.FlushAsync(cancellationToken);

    private void WaitToWrite(QueuedEntry entry)
    {
        var deadline = _enqueueWaitTimeout > TimeSpan.Zero
            ? Environment.TickCount64 + (long)_enqueueWaitTimeout.TotalMilliseconds
            : long.MaxValue;

        var spinner = new SpinWait();
        while (!_channel.Writer.TryWrite(entry))
        {
            if (_channel.Reader.Completion.IsCompleted)
            {
                RecordDropAfterDispose();
                _completionTracker.Complete(entry.Sequence);
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                RecordBackpressureDrop();
                _completionTracker.Complete(entry.Sequence);
                return;
            }

            if (spinner.NextSpinWillYield)
            {
                var remaining = deadline == long.MaxValue
                    ? Timeout.InfiniteTimeSpan
                    : TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64));
                using var cts = remaining == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(remaining);
                try
                {
                    if (!_channel.Writer.WaitToWriteAsync(cts?.Token ?? CancellationToken.None).AsTask().GetAwaiter().GetResult())
                    {
                        RecordDropAfterDispose();
                        _completionTracker.Complete(entry.Sequence);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    RecordBackpressureDrop();
                    _completionTracker.Complete(entry.Sequence);
                    return;
                }
                catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
                {
                    RecordChannelFault(ex);
                    _completionTracker.Complete(entry.Sequence);
                    return;
                }
            }
            spinner.SpinOnce();
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var queued in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    lock (SynchronizationLock)
                    {
                        _entryRenderer.Render(queued.Entry);
                    }
                }
                finally
                {
                    _completionTracker.Complete(queued.Sequence);
                }
            }
        }
        catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
        {
            LogWriterDiagnostics.Emit($"MEL.Spectre: consumer fault: {ex}");
            _channel.Writer.TryComplete(ex);
        }
        finally
        {
            while (_channel.Reader.TryRead(out var queued))
            {
                _completionTracker.Complete(queued.Sequence);
            }

            lock (SynchronizationLock)
            {
                _entryRenderer.CloseAllScopes();
            }
        }
    }

    private void OnItemDropped(QueuedEntry entry)
    {
        RecordBackpressureDrop();
        _completionTracker.Complete(entry.Sequence);
    }

    private void RecordDropAfterDispose()
    {
        Interlocked.Increment(ref _droppedAfterDispose);
        if (_droppedAfterDisposeWarning.TrySet())
        {
            LogWriterDiagnostics.Emit("MEL.Spectre: log entry dropped after provider disposal.");
        }
    }

    private void RecordBackpressureDrop()
    {
        Interlocked.Increment(ref _droppedBackpressure);
        if (_droppedBackpressureWarning.TrySet())
        {
            LogWriterDiagnostics.Emit($"MEL.Spectre: log entry dropped due to backpressure ({_backpressureMode}); consider raising ChannelCapacity or EnqueueWaitTimeout.");
        }
    }

    private void RecordChannelFault(Exception ex)
    {
        Interlocked.Increment(ref _droppedChannelFault);
        if (_droppedChannelFaultWarning.TrySet())
        {
            LogWriterDiagnostics.Emit($"MEL.Spectre: log entry dropped due to channel fault: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_channel.Writer.TryComplete())
        {
            return;
        }

        try
        {
            await _consumerTask.WaitAsync(_drainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var timeout = new TimeoutException($"MEL.Spectre: drain timeout after {_drainTimeout}; some log entries may be lost.");
            if (_drainTimeoutWarning.TrySet())
            {
                LogWriterDiagnostics.Emit(timeout.Message);
            }

            _completionTracker.Fail(timeout);

            if (Monitor.TryEnter(SynchronizationLock))
            {
                try
                {
                    _entryRenderer.CloseAllScopes();
                }
                finally
                {
                    Monitor.Exit(SynchronizationLock);
                }
            }
        }
    }

    private readonly record struct QueuedEntry(LogEntry Entry, long Sequence);
}
