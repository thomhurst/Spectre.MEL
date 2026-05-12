using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace MEL.Spectre.Theme;

public sealed class PlaceholderStyleResolver
{
    private readonly Dictionary<string, Style> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Regex Pattern, Style Style)> _byNamePattern = new();
    private readonly Dictionary<Type, Style> _byType = new();
    private readonly ConcurrentDictionary<string, Style?> _nameCache = new(StringComparer.Ordinal);
    private readonly Lock _mutationLock = new();
    private bool _frozen;
    private Style _defaultStyle = new(Color.Grey85);
    private Style _nullStyle = new(Color.Grey50, decoration: Decoration.Dim);

    public Style DefaultStyle
    {
        get => _defaultStyle;
        set => Set(ref _defaultStyle, value);
    }

    public Style NullStyle
    {
        get => _nullStyle;
        set => Set(ref _nullStyle, value);
    }

    public PlaceholderStyleResolver ForName(string name, Style style)
    {
        lock (_mutationLock)
        {
            EnsureMutable();
            _byName[name] = style;
        }
        return this;
    }

    public PlaceholderStyleResolver ForName(string name, Color color) => ForName(name, new Style(color));

    public PlaceholderStyleResolver ForNamePattern(string regex, Style style)
    {
        lock (_mutationLock)
        {
            EnsureMutable();
            _byNamePattern.Add((new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled), style));
        }
        return this;
    }

    public PlaceholderStyleResolver ForType<T>(Style style)
    {
        lock (_mutationLock)
        {
            EnsureMutable();
            _byType[typeof(T)] = style;
        }
        return this;
    }

    public PlaceholderStyleResolver ForType<T>(Color color) => ForType<T>(new Style(color));

    public PlaceholderStyleResolver ForType(Type type, Style style)
    {
        lock (_mutationLock)
        {
            EnsureMutable();
            _byType[type] = style;
        }
        return this;
    }

    public Style Resolve(string name, object? value)
    {
        Freeze();

        if (value is null)
        {
            return _nullStyle;
        }

        var nameStyle = _nameCache.GetOrAdd(name, ResolveByName);
        if (nameStyle.HasValue)
        {
            return nameStyle.Value;
        }

        var type = value.GetType();
        if (_byType.TryGetValue(type, out var typed))
        {
            return typed;
        }

        if (type.IsEnum && _byType.TryGetValue(typeof(Enum), out var enumStyle))
        {
            return enumStyle;
        }

        return _defaultStyle;
    }

    internal void Freeze()
    {
        if (Volatile.Read(ref _frozen))
        {
            return;
        }
        lock (_mutationLock)
        {
            _frozen = true;
        }
    }

    private void Set<T>(ref T field, T value)
    {
        lock (_mutationLock)
        {
            EnsureMutable();
            field = value;
        }
    }

    private void EnsureMutable()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("PlaceholderStyleResolver is frozen after the provider starts. Configure all rules before AddSpectreConsole resolves the provider.");
        }
    }

    private Style? ResolveByName(string name)
    {
        if (_byName.TryGetValue(name, out var named))
        {
            return named;
        }

        foreach (var (pattern, style) in _byNamePattern)
        {
            if (pattern.IsMatch(name))
            {
                return style;
            }
        }

        return null;
    }
}
