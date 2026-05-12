namespace MEL.Spectre;

/// <summary>
/// Single-shot latch. Must live as a field on the owning type — copying by
/// value forks the latch and breaks the at-most-once contract.
/// </summary>
/// <remarks><see cref="TrySet"/> returns <c>true</c> for the first caller only.</remarks>
internal struct OnceFlag
{
    private int _value;

    public bool TrySet() => Interlocked.CompareExchange(ref _value, 1, 0) == 0;

    public bool IsSet => Volatile.Read(ref _value) != 0;
}
