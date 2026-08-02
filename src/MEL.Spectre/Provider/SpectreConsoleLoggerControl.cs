using Microsoft.Extensions.Logging;

namespace MEL.Spectre.Provider;

internal sealed class SpectreConsoleLoggerControl : ISpectreConsoleLoggerControl
{
    private readonly SpectreConsoleLoggerProvider _provider;

    public SpectreConsoleLoggerControl(IEnumerable<ILoggerProvider> providers)
    {
        _provider = providers.OfType<SpectreConsoleLoggerProvider>().Single();
    }

    public object SynchronizationLock => _provider.SynchronizationLock;

    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        _provider.FlushAsync(cancellationToken);

    public bool TryAcquireRenderGate(TimeSpan timeout, out IDisposable? gate) =>
        _provider.TryAcquireRenderGate(timeout, out gate);

    public ValueTask<IDisposable?> TryAcquireRenderGateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        _provider.TryAcquireRenderGateAsync(timeout, cancellationToken);
}
