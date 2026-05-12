using System.Threading.Channels;
using Spectre.Console;
using Spectre.MEL.Ci;
using Spectre.MEL.Scopes;

namespace Spectre.MEL.Provider;

internal sealed class BackgroundWriter : IAsyncDisposable
{
    private readonly Channel<LogEntry> _channel;
    private readonly IAnsiConsole _console;
    private readonly ICiRenderer _renderer;
    private readonly BackpressureMode _backpressureMode;
    private readonly TimeSpan _drainTimeout;
    private readonly TimeSpan _enqueueWaitTimeout;
    private readonly Task _consumerTask;
    private readonly Stack<ScopeFrame> _activeScopes = new();
    private long _droppedAfterDispose;
    private long _droppedBackpressure;
    private int _droppedWarningEmitted;
    private int _drainTimeoutEmitted;

    public BackgroundWriter(
        IAnsiConsole console,
        ICiRenderer renderer,
        int capacity,
        BackpressureMode backpressureMode,
        TimeSpan drainTimeout,
        TimeSpan enqueueWaitTimeout)
    {
        _console = console;
        _renderer = renderer;
        _backpressureMode = backpressureMode;
        _drainTimeout = drainTimeout;
        _enqueueWaitTimeout = enqueueWaitTimeout;

        var fullMode = backpressureMode switch
        {
            BackpressureMode.DropNewest => BoundedChannelFullMode.DropWrite,
            BackpressureMode.DropOldest => BoundedChannelFullMode.DropOldest,
            _ => BoundedChannelFullMode.Wait,
        };

        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = fullMode,
        });

        _consumerTask = Task.Run(ConsumeAsync);
    }

    public long DroppedAfterDisposeCount => Interlocked.Read(ref _droppedAfterDispose);

    public long DroppedBackpressureCount => Interlocked.Read(ref _droppedBackpressure);

    public void Enqueue(LogEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
        {
            return;
        }

        if (_channel.Reader.Completion.IsCompleted)
        {
            RecordDropAfterDispose();
            return;
        }

        if (_backpressureMode != BackpressureMode.Wait)
        {
            Interlocked.Increment(ref _droppedBackpressure);
            return;
        }

        WaitToWrite(entry);
    }

    private void WaitToWrite(LogEntry entry)
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
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                Interlocked.Increment(ref _droppedBackpressure);
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
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _droppedBackpressure);
                    return;
                }
            }
            spinner.SpinOnce();
        }
    }

    private void RecordDropAfterDispose()
    {
        Interlocked.Increment(ref _droppedAfterDispose);
        if (Interlocked.Exchange(ref _droppedWarningEmitted, 1) == 0)
        {
            TryWriteStderr("Spectre.MEL: log entry dropped after provider disposal.");
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    ReconcileScopes(entry.Scopes);
                    _renderer.RenderEntry(_console, entry, _activeScopes.Count);
                }
                catch (Exception ex) when (!IsFatal(ex))
                {
                    TryWriteStderr($"Spectre.MEL: render fault: {ex}");
                }
            }
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            TryWriteStderr($"Spectre.MEL: consumer fault: {ex}");
        }
        finally
        {
            try
            {
                CloseAllScopes();
            }
            catch (Exception ex) when (!IsFatal(ex))
            {
                TryWriteStderr($"Spectre.MEL: scope close fault: {ex}");
            }
        }
    }

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException
        or StackOverflowException
        or AccessViolationException
        or ThreadAbortException;

    private static void TryWriteStderr(string message)
    {
        try
        {
            System.Console.Error.WriteLine(message);
        }
        catch
        {
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

    private void CloseAllScopes()
    {
        while (_activeScopes.Count > 0)
        {
            var frame = _activeScopes.Pop();
            _renderer.CloseScope(_console, frame, _activeScopes.Count);
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
            if (Interlocked.Exchange(ref _drainTimeoutEmitted, 1) == 0)
            {
                TryWriteStderr($"Spectre.MEL: drain timeout after {_drainTimeout}; some log entries may be lost.");
            }
            try
            {
                CloseAllScopes();
            }
            catch (Exception ex) when (!IsFatal(ex))
            {
                TryWriteStderr($"Spectre.MEL: scope close fault on drain timeout: {ex}");
            }
        }
    }
}
