using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Spectre.MEL.Theme;

public sealed class PlaceholderStyleResolver
{
    private readonly Dictionary<string, Style> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Regex Pattern, Style Style)> _byNamePattern = new();
    private readonly Dictionary<Type, Style> _byType = new();
    private readonly ConcurrentDictionary<string, Style?> _nameCache = new(StringComparer.Ordinal);

    public Style DefaultStyle { get; set; } = new(Color.Grey85);
    public Style NullStyle { get; set; } = new(Color.Grey50, decoration: Decoration.Dim);

    public PlaceholderStyleResolver ForName(string name, Style style)
    {
        _byName[name] = style;
        _nameCache.Clear();
        return this;
    }

    public PlaceholderStyleResolver ForName(string name, Color color) => ForName(name, new Style(color));

    public PlaceholderStyleResolver ForNamePattern(string regex, Style style)
    {
        _byNamePattern.Add((new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled), style));
        _nameCache.Clear();
        return this;
    }

    public PlaceholderStyleResolver ForType<T>(Style style)
    {
        _byType[typeof(T)] = style;
        return this;
    }

    public PlaceholderStyleResolver ForType<T>(Color color) => ForType<T>(new Style(color));

    public PlaceholderStyleResolver ForType(Type type, Style style)
    {
        _byType[type] = style;
        return this;
    }

    public Style Resolve(string name, object? value)
    {
        if (value is null)
        {
            return NullStyle;
        }

        var nameStyle = _nameCache.GetOrAdd(name, ResolveByName);
        if (nameStyle is not null)
        {
            return nameStyle;
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

        return DefaultStyle;
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
