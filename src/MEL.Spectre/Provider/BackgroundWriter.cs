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
    private readonly object _completionGate = new();
    private readonly List<SequenceRange> _completedRanges = [];
    private readonly List<FlushWaiter> _flushWaiters = [];
    private Exception? _terminalFailure;
    private long _lastSequence;
    private long _completedSequence;
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

    internal int PendingCompletionRangeCount
    {
        get
        {
            lock (_completionGate)
            {
                return _completedRanges.Count;
            }
        }
    }

    public void Enqueue(LogEntry entry)
    {
        var queued = new QueuedEntry(entry, Interlocked.Increment(ref _lastSequence));
        if (_channel.Writer.TryWrite(queued))
        {
            return;
        }

        if (_channel.Reader.Completion.IsCompleted)
        {
            RecordDropAfterDispose();
            CompleteSequence(queued.Sequence);
            return;
        }

        if (_backpressureMode != BackpressureMode.Wait)
        {
            RecordBackpressureDrop();
            CompleteSequence(queued.Sequence);
            return;
        }

        WaitToWrite(queued);
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var target = Interlocked.Read(ref _lastSequence);
        Task flushTask;
        lock (_completionGate)
        {
            if (_completedSequence >= target)
            {
                return Task.CompletedTask;
            }

            if (_terminalFailure is not null)
            {
                return Task.FromException(_terminalFailure);
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _flushWaiters.Add(new FlushWaiter(target, completion));
            flushTask = completion.Task;
        }

        return cancellationToken.CanBeCanceled
            ? flushTask.WaitAsync(cancellationToken)
            : flushTask;
    }

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
                CompleteSequence(entry.Sequence);
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                RecordBackpressureDrop();
                CompleteSequence(entry.Sequence);
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
                        CompleteSequence(entry.Sequence);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    RecordBackpressureDrop();
                    CompleteSequence(entry.Sequence);
                    return;
                }
                catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
                {
                    RecordChannelFault(ex);
                    CompleteSequence(entry.Sequence);
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
                    CompleteSequence(queued.Sequence);
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
                CompleteSequence(queued.Sequence);
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
        CompleteSequence(entry.Sequence);
    }

    private void CompleteSequence(long sequence)
    {
        List<TaskCompletionSource>? ready;
        lock (_completionGate)
        {
            if (sequence <= _completedSequence)
            {
                return;
            }

            if (sequence == _completedSequence + 1)
            {
                _completedSequence = sequence;
                while (_completedRanges.Count > 0 && _completedRanges[0].Start == _completedSequence + 1)
                {
                    _completedSequence = _completedRanges[0].End;
                    _completedRanges.RemoveAt(0);
                }
            }
            else
            {
                AddCompletedRange(sequence);
            }

            ready = TakeReadyFlushWaiters();
        }

        CompleteFlushWaiters(ready);
    }

    private void AddCompletedRange(long sequence)
    {
        for (var i = 0; i < _completedRanges.Count; i++)
        {
            var range = _completedRanges[i];
            if (sequence < range.Start - 1)
            {
                _completedRanges.Insert(i, new SequenceRange(sequence, sequence));
                return;
            }

            if (sequence == range.Start - 1)
            {
                _completedRanges[i] = range with { Start = sequence };
                return;
            }

            if (sequence <= range.End)
            {
                return;
            }

            if (sequence == range.End + 1)
            {
                var end = sequence;
                if (i + 1 < _completedRanges.Count && _completedRanges[i + 1].Start == sequence + 1)
                {
                    end = _completedRanges[i + 1].End;
                    _completedRanges.RemoveAt(i + 1);
                }

                _completedRanges[i] = range with { End = end };
                return;
            }
        }

        _completedRanges.Add(new SequenceRange(sequence, sequence));
    }

    private void FailPendingFlushWaiters(Exception exception)
    {
        TaskCompletionSource[] pending;
        lock (_completionGate)
        {
            _terminalFailure ??= exception;
            pending = new TaskCompletionSource[_flushWaiters.Count];
            for (var i = 0; i < _flushWaiters.Count; i++)
            {
                pending[i] = _flushWaiters[i].Completion;
            }
            _flushWaiters.Clear();
        }

        for (var i = 0; i < pending.Length; i++)
        {
            pending[i].TrySetException(exception);
        }
    }

    private List<TaskCompletionSource>? TakeReadyFlushWaiters()
    {
        List<TaskCompletionSource>? ready = null;
        for (var i = _flushWaiters.Count - 1; i >= 0; i--)
        {
            if (_flushWaiters[i].Target > _completedSequence)
            {
                continue;
            }

            ready ??= [];
            ready.Add(_flushWaiters[i].Completion);
            _flushWaiters.RemoveAt(i);
        }

        return ready;
    }

    private static void CompleteFlushWaiters(List<TaskCompletionSource>? ready)
    {
        if (ready is null)
        {
            return;
        }

        for (var i = 0; i < ready.Count; i++)
        {
            ready[i].TrySetResult();
        }
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

            FailPendingFlushWaiters(timeout);

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

    private readonly record struct SequenceRange(long Start, long End);

    private readonly record struct FlushWaiter(long Target, TaskCompletionSource Completion);
}
