using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Spectre.MEL.Masking;

internal sealed class SecretMasker
{
    private const string MaskedToken = "***";

    private readonly Regex[] _patterns;
    private readonly ConcurrentDictionary<string, bool> _shouldMaskCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _emitted = new();
    private readonly int _capacity;

    public SecretMasker(IEnumerable<string> patterns, int valueCacheCapacity)
    {
        _patterns = patterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();
        _capacity = Math.Max(0, valueCacheCapacity);
    }

    public bool ShouldMask(string name) => _shouldMaskCache.GetOrAdd(name, MatchPatterns);

    private bool MatchPatterns(string name)
    {
        for (var i = 0; i < _patterns.Length; i++)
        {
            if (_patterns[i].IsMatch(name))
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
