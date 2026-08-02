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
    /// Gets the gate used while MEL.Spectre renders. Lock this object around direct console writes to prevent
    /// physical-line interleaving.
    /// </summary>
    object SynchronizationLock { get; }
}
