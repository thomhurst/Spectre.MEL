using System.Diagnostics.CodeAnalysis;

namespace MEL.Spectre.Provider;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Caller-owned leases may outlive provider disposal; SemaphoreSlim has no allocated wait handle.")]
internal sealed class RenderGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _ownerThreadId;
    private int _recursionCount;

    public void Enter()
    {
        var threadId = Environment.CurrentManagedThreadId;
        if (Volatile.Read(ref _ownerThreadId) == threadId)
        {
            _recursionCount++;
            return;
        }

        _semaphore.Wait();
        _recursionCount = 1;
        Volatile.Write(ref _ownerThreadId, threadId);
    }

    public void Exit()
    {
        if (Volatile.Read(ref _ownerThreadId) != Environment.CurrentManagedThreadId)
        {
            throw new SynchronizationLockException("The render gate can only be exited by its owning thread.");
        }

        if (--_recursionCount > 0)
        {
            return;
        }

        Volatile.Write(ref _ownerThreadId, 0);
        _semaphore.Release();
    }

    public bool TryAcquire(TimeSpan timeout, out IDisposable? gate)
    {
        if (!_semaphore.Wait(timeout))
        {
            gate = null;
            return false;
        }

        gate = new Lease(_semaphore);
        return true;
    }

    public async ValueTask<IDisposable?> TryAcquireAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
