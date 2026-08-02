using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace MEL.Spectre.Masking;

internal sealed class SecretMasker
{
    private const string MaskedToken = "***";
    private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Regex[] _namePatterns;
    private readonly Regex[] _valuePatterns;
    private readonly ConcurrentDictionary<string, bool> _shouldMaskCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _emitted = new();
    private readonly int _capacity;

    public SecretMasker(IEnumerable<string> namePatterns, int valueCacheCapacity)
        : this(namePatterns, Array.Empty<string>(), valueCacheCapacity)
    {
    }

    public SecretMasker(IEnumerable<string> namePatterns, IEnumerable<string> valuePatterns, int valueCacheCapacity, TimeSpan? regexTimeout = null)
    {
        var timeout = regexTimeout ?? DefaultRegexTimeout;
        _namePatterns = namePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, timeout))
            .ToArray();
        _valuePatterns = valuePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, timeout))
            .ToArray();
        _capacity = Math.Max(0, valueCacheCapacity);
    }

    public bool HasValuePatterns => _valuePatterns.Length > 0;

    public bool ShouldMask(string name) => _shouldMaskCache.GetOrAdd(name, MatchNamePatterns);

    public bool ShouldMaskValue(string value)
    {
        for (var i = 0; i < _valuePatterns.Length; i++)
        {
            if (IsMatch(_valuePatterns[i], value))
            {
                return true;
            }
        }
        return false;
    }

    public bool TryMaskValuePatterns(string value, List<string> maskValueSink, out string maskedValue)
    {
        maskedValue = value;
        var ranges = new List<MaskRange>();
        var sinkStart = maskValueSink.Count;

        for (var patternIndex = 0; patternIndex < _valuePatterns.Length; patternIndex++)
        {
            foreach (Match match in _valuePatterns[patternIndex].Matches(value))
            {
                if (match.Length == 0)
                {
                    maskValueSink.RemoveRange(sinkStart, maskValueSink.Count - sinkStart);
                    if (value.Length > 0)
                    {
                        maskValueSink.Add(value);
                    }
                    maskedValue = MaskedToken;
                    return true;
                }

                ranges.Add(new MaskRange(match.Index, match.Index + match.Length));
                maskValueSink.Add(match.Value);
            }
        }

        if (ranges.Count == 0)
        {
            return false;
        }

        ranges.Sort(static (left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            return byStart != 0 ? byStart : right.End.CompareTo(left.End);
        });

        var mergedRanges = new List<MaskRange>(ranges.Count);
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            if (mergedRanges.Count > 0 && range.Start <= mergedRanges[^1].End)
            {
                var previous = mergedRanges[^1];
                if (range.End > previous.End)
                {
                    mergedRanges[^1] = previous with { End = range.End };
                }
                continue;
            }

            mergedRanges.Add(range);
        }

        var builder = new StringBuilder(value.Length);
        var position = 0;
        for (var i = 0; i < mergedRanges.Count; i++)
        {
            var range = mergedRanges[i];
            builder.Append(value, position, range.Start - position);
            builder.Append(MaskedToken);
            position = range.End;
        }
        builder.Append(value, position, value.Length - position);
        maskedValue = builder.ToString();
        return true;
    }

    public string MaskValuePatterns(string value, List<string>? matchedValues = null)
    {
        var ranges = GetValuePatternMaskRanges(value, matchedValues);
        if (ranges.Count == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var copiedLength = 0;
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            builder.Append(value.AsSpan(copiedLength, range.Start - copiedLength));
            builder.Append(MaskedToken);
            copiedLength = range.End;
        }
        builder.Append(value.AsSpan(copiedLength));
        return builder.ToString();
    }

    internal List<MaskRange> GetValuePatternMaskRanges(string value, List<string>? matchedValues = null)
    {
        var ranges = new List<MaskRange>();
        for (var patternIndex = 0; patternIndex < _valuePatterns.Length; patternIndex++)
        {
            try
            {
                foreach (Match match in _valuePatterns[patternIndex].Matches(value))
                {
                    ranges.Add(new MaskRange(match.Index, match.Index + match.Length));
                    matchedValues?.Add(match.Value);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A user-supplied pattern must not stall the single rendering consumer.
            }
        }

        if (ranges.Count < 2)
        {
            return ranges;
        }

        ranges.Sort(static (left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            return byStart != 0 ? byStart : right.End.CompareTo(left.End);
        });

        var writeIndex = 0;
        for (var readIndex = 1; readIndex < ranges.Count; readIndex++)
        {
            var current = ranges[readIndex];
            var previous = ranges[writeIndex];
            if (current.Start <= previous.End)
            {
                if (current.End > previous.End)
                {
                    ranges[writeIndex] = previous with { End = current.End };
                }
                continue;
            }

            ranges[++writeIndex] = current;
        }
        ranges.RemoveRange(writeIndex + 1, ranges.Count - writeIndex - 1);
        return ranges;
    }

    private bool MatchNamePatterns(string name)
    {
        for (var i = 0; i < _namePatterns.Length; i++)
        {
            if (IsMatch(_namePatterns[i], name))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsMatch(Regex pattern, string value)
    {
        try
        {
            return pattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
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

    internal readonly record struct MaskRange(int Start, int End);
}
