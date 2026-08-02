using System.Diagnostics.CodeAnalysis;

namespace MEL.Spectre.Provider;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Caller-owned leases may outlive provider disposal; SemaphoreSlim has no allocated wait handle.")]
internal sealed class RenderGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public void Enter() => _semaphore.Wait();

    public async ValueTask EnterAsync(CancellationToken cancellationToken) =>
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    public void Exit() => _semaphore.Release();

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
