using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MEL.Spectre.Masking;

internal sealed class SecretMasker
{
    private const string MaskedToken = "***";

    private readonly Regex[] _namePatterns;
    private readonly Regex[] _valuePatterns;
    private readonly ConcurrentDictionary<string, bool> _shouldMaskCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _emitted = new();
    private readonly int _capacity;

    public SecretMasker(IEnumerable<string> namePatterns, int valueCacheCapacity)
        : this(namePatterns, Array.Empty<string>(), valueCacheCapacity)
    {
    }

    public SecretMasker(IEnumerable<string> namePatterns, IEnumerable<string> valuePatterns, int valueCacheCapacity)
    {
        _namePatterns = namePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();
        _valuePatterns = valuePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();
        _capacity = Math.Max(0, valueCacheCapacity);
    }

    public bool HasValuePatterns => _valuePatterns.Length > 0;

    public bool ShouldMask(string name) => _shouldMaskCache.GetOrAdd(name, MatchNamePatterns);

    public bool ShouldMaskValue(string value)
    {
        for (var i = 0; i < _valuePatterns.Length; i++)
        {
            if (_valuePatterns[i].IsMatch(value))
            {
                return true;
            }
        }
        return false;
    }

    private bool MatchNamePatterns(string name)
    {
        for (var i = 0; i < _namePatterns.Length; i++)
        {
            if (_namePatterns[i].IsMatch(name))
            {
                return true;
            }
        }
        return false;
    }

    public static string Mask(object? value)
    {
        if (value is null || value is string)
        {
            return MaskedToken;
        }
        return string.Concat(value.GetType().Name, ":", MaskedToken);
    }

    public bool TryRegisterForEmission(string value)
    {
        if (_capacity == 0 || _emitted.Count >= _capacity)
        {
            return false;
        }
        return _emitted.TryAdd(value, 0);
    }
}
