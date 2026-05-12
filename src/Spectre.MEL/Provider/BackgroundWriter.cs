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
    private readonly Task _consumerTask;
    private readonly Stack<ScopeFrame> _activeScopes = new();

    public BackgroundWriter(
        IAnsiConsole console,
        ICiRenderer renderer,
        int capacity,
        BackpressureMode backpressureMode,
        TimeSpan drainTimeout)
    {
        _console = console;
        _renderer = renderer;
        _backpressureMode = backpressureMode;
        _drainTimeout = drainTimeout;

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

    public void Enqueue(LogEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
        {
            return;
        }

        if (_backpressureMode != BackpressureMode.Wait)
        {
            return;
        }

        var spinner = new SpinWait();
        while (!_channel.Writer.TryWrite(entry))
        {
            if (spinner.NextSpinWillYield)
            {
                if (!_channel.Writer.WaitToWriteAsync().AsTask().GetAwaiter().GetResult())
                {
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
            await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                ReconcileScopes(entry.Scopes);
                _renderer.RenderEntry(_console, entry, _activeScopes.Count);
            }
        }
        catch (Exception ex)
        {
            try
            {
                _console.WriteLine($"[Spectre.MEL writer fault] {ex}");
            }
            catch
            {
            }
        }
        finally
        {
            CloseAllScopes();
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
        }
    }
}
