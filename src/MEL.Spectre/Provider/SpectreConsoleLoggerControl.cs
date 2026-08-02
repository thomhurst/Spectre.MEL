using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MEL.Spectre.Provider;

internal sealed class SpectreConsoleLoggerControl : ISpectreConsoleLoggerControl
{
    private readonly SpectreConsoleLoggerProvider _provider;
    private readonly SpectreConsoleLoggerSuspension _suspension;
    private readonly IOptionsMonitor<LoggerFilterOptions> _filterOptions;

    public SpectreConsoleLoggerControl(
        IEnumerable<ILoggerProvider> providers,
        SpectreConsoleLoggerSuspension suspension,
        IOptionsMonitor<LoggerFilterOptions> filterOptions)
    {
        _provider = providers.OfType<SpectreConsoleLoggerProvider>().Single();
        _suspension = suspension;
        _filterOptions = filterOptions;
    }

    public object SynchronizationLock => _provider.SynchronizationLock;

    public IDisposable Suspend() => _suspension.Suspend();

    public bool WouldRender(string categoryName, LogLevel logLevel)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        if (logLevel == LogLevel.None)
        {
            return false;
        }

        return SpectreConsoleLoggerFilter.IsEnabled(_filterOptions.CurrentValue, categoryName, logLevel);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        _provider.FlushAsync(cancellationToken);

    public bool TryAcquireRenderGate(TimeSpan timeout, out IDisposable? gate) =>
        _provider.TryAcquireRenderGate(timeout, out gate);

    public ValueTask<IDisposable?> TryAcquireRenderGateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        _provider.TryAcquireRenderGateAsync(timeout, cancellationToken);
}
