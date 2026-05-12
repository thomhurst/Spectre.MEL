using System.Diagnostics;
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
    private long _droppedChannelFault;
    private OnceFlag _droppedAfterDisposeWarning;
    private OnceFlag _droppedBackpressureWarning;
    private OnceFlag _droppedChannelFaultWarning;
    private OnceFlag _drainTimeoutWarning;
    private OnceFlag _scopesClosed;

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

    public long DroppedChannelFaultCount => Interlocked.Read(ref _droppedChannelFault);

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
            RecordBackpressureDrop();
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
                RecordBackpressureDrop();
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
                    RecordBackpressureDrop();
                    return;
                }
                catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
                {
                    RecordChannelFault(ex);
                    return;
                }
            }
            spinner.SpinOnce();
        }
    }

    private void RecordDropAfterDispose()
    {
        Interlocked.Increment(ref _droppedAfterDispose);
        if (_droppedAfterDisposeWarning.TrySet())
        {
            EmitDiagnostic("Spectre.MEL: log entry dropped after provider disposal.");
        }
    }

    private void RecordBackpressureDrop()
    {
        Interlocked.Increment(ref _droppedBackpressure);
        if (_droppedBackpressureWarning.TrySet())
        {
            EmitDiagnostic($"Spectre.MEL: log entry dropped due to backpressure ({_backpressureMode}); consider raising ChannelCapacity or EnqueueWaitTimeout.");
        }
    }

    private void RecordChannelFault(Exception ex)
    {
        Interlocked.Increment(ref _droppedChannelFault);
        if (_droppedChannelFaultWarning.TrySet())
        {
            EmitDiagnostic($"Spectre.MEL: log entry dropped due to channel fault: {ex.GetType().Name}: {ex.Message}");
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
                catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
                {
                    EmitDiagnostic($"Spectre.MEL: render fault: {ex}");
                }
            }
        }
        catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
        {
            EmitDiagnostic($"Spectre.MEL: consumer fault: {ex}");
        }
        finally
        {
            TryCloseAllScopes();
        }
    }

    private void TryCloseAllScopes()
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
            EmitDiagnostic($"Spectre.MEL: scope close fault: {ex}");
        }
    }

    private static void EmitDiagnostic(string message)
    {
        var written = false;
        try
        {
            System.Console.Error.WriteLine(message);
            written = true;
        }
        catch
        {
        }

        if (!written)
        {
            try
            {
                Debug.WriteLine(message);
            }
            catch
            {
            }
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
            if (_drainTimeoutWarning.TrySet())
            {
                EmitDiagnostic($"Spectre.MEL: drain timeout after {_drainTimeout}; some log entries may be lost.");
            }
            TryCloseAllScopes();
        }
    }
}
