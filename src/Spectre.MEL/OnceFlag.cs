namespace Spectre.MEL;

internal struct OnceFlag
{
    private int _value;

    public bool TrySet() => Interlocked.CompareExchange(ref _value, 1, 0) == 0;

    public bool IsSet => Volatile.Read(ref _value) != 0;
}
