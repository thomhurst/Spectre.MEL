namespace MEL.Spectre.Provider;

internal sealed class SpectreConsoleLoggerSuspension
{
    private readonly AsyncLocal<int> _depth = new();

    public bool IsSuspended => _depth.Value > 0;

    public IDisposable Suspend()
    {
        _depth.Value++;
        return new SuspensionScope(this);
    }

    private sealed class SuspensionScope(SpectreConsoleLoggerSuspension owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._depth.Value--;
            }
        }
    }
}
