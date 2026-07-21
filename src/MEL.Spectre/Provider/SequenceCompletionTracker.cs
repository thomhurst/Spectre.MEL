namespace MEL.Spectre.Provider;

internal sealed class SequenceCompletionTracker
{
    private readonly object _gate = new();
    private readonly List<SequenceRange> _completedRanges = [];
    private readonly List<FlushWaiter> _waiters = [];
    private Exception? _terminalFailure;
    private long _lastSequence;
    private long _completedSequence;
    private int _pendingEntryCount;

    public int PendingCompletionRangeCount
    {
        get
        {
            lock (_gate)
            {
                return _completedRanges.Count;
            }
        }
    }

    public int PendingFlushWaiterCount
    {
        get
        {
            lock (_gate)
            {
                return _waiters.Count;
            }
        }
    }

    public int PendingEntryCount => Volatile.Read(ref _pendingEntryCount);

    public long Begin()
    {
        var sequence = Interlocked.Increment(ref _lastSequence);
        Interlocked.Increment(ref _pendingEntryCount);
        return sequence;
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var target = Interlocked.Read(ref _lastSequence);
        FlushWaiter waiter;
        lock (_gate)
        {
            if (_completedSequence >= target)
            {
                return Task.CompletedTask;
            }

            if (_terminalFailure is not null)
            {
                return Task.FromException(_terminalFailure);
            }

            waiter = new FlushWaiter(target);
            _waiters.Add(waiter);
        }

        return cancellationToken.CanBeCanceled
            ? WaitWithCancellationAsync(waiter, cancellationToken)
            : waiter.Completion.Task;
    }

    public void Complete(long sequence)
    {
        List<TaskCompletionSource>? ready;
        lock (_gate)
        {
            if (!RecordCompletion(sequence))
            {
                return;
            }

            ready = TakeReadyWaiters();
        }

        Interlocked.Decrement(ref _pendingEntryCount);
        CompleteWaiters(ready);
    }

    public void Fail(Exception exception)
    {
        TaskCompletionSource[] pending;
        lock (_gate)
        {
            _terminalFailure ??= exception;
            pending = new TaskCompletionSource[_waiters.Count];
            for (var i = 0; i < _waiters.Count; i++)
            {
                pending[i] = _waiters[i].Completion;
            }
            _waiters.Clear();
        }

        for (var i = 0; i < pending.Length; i++)
        {
            pending[i].TrySetException(exception);
        }
    }

    private async Task WaitWithCancellationAsync(FlushWaiter waiter, CancellationToken cancellationToken)
    {
        try
        {
            await waiter.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                _waiters.Remove(waiter);
            }

            waiter.Completion.TrySetCanceled(cancellationToken);
            throw;
        }
    }

    private bool RecordCompletion(long sequence)
    {
        if (sequence <= _completedSequence)
        {
            return false;
        }

        if (sequence == _completedSequence + 1)
        {
            _completedSequence = sequence;
            while (_completedRanges.Count > 0 && _completedRanges[0].Start == _completedSequence + 1)
            {
                _completedSequence = _completedRanges[0].End;
                _completedRanges.RemoveAt(0);
            }
            return true;
        }

        return AddCompletedRange(sequence);
    }

    private bool AddCompletedRange(long sequence)
    {
        for (var i = 0; i < _completedRanges.Count; i++)
        {
            var range = _completedRanges[i];
            if (sequence < range.Start - 1)
            {
                _completedRanges.Insert(i, new SequenceRange(sequence, sequence));
                return true;
            }

            if (sequence == range.Start - 1)
            {
                _completedRanges[i] = range with { Start = sequence };
                return true;
            }

            if (sequence <= range.End)
            {
                return false;
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
                return true;
            }
        }

        _completedRanges.Add(new SequenceRange(sequence, sequence));
        return true;
    }

    private List<TaskCompletionSource>? TakeReadyWaiters()
    {
        List<TaskCompletionSource>? ready = null;
        for (var i = _waiters.Count - 1; i >= 0; i--)
        {
            if (_waiters[i].Target > _completedSequence)
            {
                continue;
            }

            ready ??= [];
            ready.Add(_waiters[i].Completion);
            _waiters.RemoveAt(i);
        }

        return ready;
    }

    private static void CompleteWaiters(List<TaskCompletionSource>? ready)
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

    private readonly record struct SequenceRange(long Start, long End);

    private sealed class FlushWaiter(long target)
    {
        public long Target { get; } = target;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
