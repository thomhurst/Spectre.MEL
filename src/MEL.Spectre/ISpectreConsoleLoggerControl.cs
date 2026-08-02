using Microsoft.Extensions.Logging;

namespace MEL.Spectre;

/// <summary>
/// Coordinates MEL.Spectre log output with direct writes to the same console.
/// </summary>
public interface ISpectreConsoleLoggerControl
{
    /// <summary>
    /// Suppresses MEL.Spectre log output in the current asynchronous context until the returned scope is disposed.
    /// </summary>
    IDisposable Suspend();

    /// <summary>
    /// Determines whether the current logging filter configuration enables MEL.Spectre for a category and level.
    /// </summary>
    bool WouldRender(string categoryName, LogLevel logLevel);

    /// <summary>
    /// Completes after every log entry accepted before this call has been rendered or dropped by the configured
    /// backpressure policy.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to pause MEL.Spectre rendering within <paramref name="timeout"/>. While the returned lease is held,
    /// lock <see cref="SynchronizationLock"/> around the direct write to coordinate with legacy callers.
    /// </summary>
    bool TryAcquireRenderGate(TimeSpan timeout, out IDisposable? gate);

    /// <summary>
    /// Asynchronously attempts to pause MEL.Spectre rendering within <paramref name="timeout"/>. Returns
    /// <see langword="null"/> on timeout. While the lease is held, lock <see cref="SynchronizationLock"/> around the
    /// direct write to coordinate with legacy callers.
    /// </summary>
    ValueTask<IDisposable?> TryAcquireRenderGateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the gate used while MEL.Spectre renders. Lock this object around direct console writes to prevent
    /// physical-line interleaving.
    /// </summary>
    object SynchronizationLock { get; }
}
