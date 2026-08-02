namespace MEL.Spectre.Provider;

internal sealed class SpectreConsoleLoggerSuspension
{
    private readonly AsyncLocal<SuspensionScope?> _current = new();

    public bool IsSuspended
    {
        get
        {
            var current = _current.Value;
            var scope = current;
            while (scope?.IsDisposed == true)
            {
                scope = scope.Parent;
            }

            if (!ReferenceEquals(current, scope))
            {
                _current.Value = scope;
            }
            return scope is not null;
        }
    }

    public IDisposable Suspend()
    {
        var scope = new SuspensionScope(this, _current.Value);
        _current.Value = scope;
        return scope;
    }

    private sealed class SuspensionScope(
        SpectreConsoleLoggerSuspension owner,
        SuspensionScope? parent) : IDisposable
    {
        private int _disposed;

        public SuspensionScope? Parent => parent;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (ReferenceEquals(owner._current.Value, this))
            {
                owner._current.Value = parent;
            }
        }
    }
}
