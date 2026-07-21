namespace MEL.Spectre.Provider;

internal interface ILogEntryWriter : IAsyncDisposable
{
    object SynchronizationLock { get; }

    void Enqueue(LogEntry entry);

    Task FlushAsync(CancellationToken cancellationToken);
}
