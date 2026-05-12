using MEL.Spectre;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class OnceFlagTests
{
    [Test]
    public async Task TrySet_first_call_returns_true_subsequent_return_false()
    {
        var flag = new Holder();
        await Assert.That(flag.Flag.IsSet).IsFalse();
        await Assert.That(flag.TrySet()).IsTrue();
        await Assert.That(flag.Flag.IsSet).IsTrue();
        await Assert.That(flag.TrySet()).IsFalse();
        await Assert.That(flag.TrySet()).IsFalse();
    }

    [Test]
    public async Task TrySet_concurrent_exactly_one_winner()
    {
        var holder = new Holder();
        var winners = 0;
        Parallel.For(0, 1024, _ =>
        {
            if (holder.TrySet())
            {
                Interlocked.Increment(ref winners);
            }
        });
        await Assert.That(winners).IsEqualTo(1);
        await Assert.That(holder.Flag.IsSet).IsTrue();
    }

    private sealed class Holder
    {
        public OnceFlag Flag;

        public bool TrySet() => Flag.TrySet();
    }
}
