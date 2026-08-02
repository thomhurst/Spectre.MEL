using System.Collections.Concurrent;
using System.Text;
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

    public bool TryMaskValuePatterns(string value, List<string> maskValueSink, out string maskedValue)
    {
        maskedValue = value;
        var found = false;

        for (var patternIndex = 0; patternIndex < _valuePatterns.Length; patternIndex++)
        {
            var matches = _valuePatterns[patternIndex].Matches(maskedValue);
            if (matches.Count == 0)
            {
                continue;
            }

            var builder = new StringBuilder(maskedValue.Length);
            var position = 0;
            foreach (Match match in matches)
            {
                builder.Append(maskedValue, position, match.Index - position);
                builder.Append(MaskedToken);
                maskValueSink.Add(match.Value);
                position = match.Index + match.Length;
            }
            builder.Append(maskedValue, position, maskedValue.Length - position);
            maskedValue = builder.ToString();
            found = true;
        }

        return found;
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
