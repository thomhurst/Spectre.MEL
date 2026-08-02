namespace MEL.Spectre.Provider;

internal static class StateReader
{
    internal const string OriginalFormatKey = "{OriginalFormat}";
    internal const string MarkupEnabledKey = "{MEL.Spectre.Markup}";

    public static (string? OriginalFormat, Placeholder[] Placeholders, bool AllowMarkup) Extract<TState>(TState state)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> list)
        {
            return ExtractFromList(list);
        }

        if (state is IEnumerable<KeyValuePair<string, object?>> enumerable)
        {
            return ExtractFromEnumerable(enumerable);
        }

        return (null, [], false);
    }

    private static (string? OriginalFormat, Placeholder[] Placeholders, bool AllowMarkup) ExtractFromList(IReadOnlyList<KeyValuePair<string, object?>> list)
    {
        if (list.Count == 0)
        {
            return (null, [], false);
        }

        string? originalFormat = null;
        var allowMarkup = false;
        var count = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var key = list[i].Key;
            if (string.Equals(key, OriginalFormatKey, StringComparison.Ordinal))
            {
                originalFormat = list[i].Value as string;
            }
            else if (string.Equals(key, MarkupEnabledKey, StringComparison.Ordinal))
            {
                allowMarkup = list[i].Value is true;
            }
            else
            {
                count++;
            }
        }

        if (count == 0)
        {
            return (originalFormat, [], allowMarkup);
        }

        var placeholders = new Placeholder[count];
        var w = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var kv = list[i];
            if (string.Equals(kv.Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                continue;
            }
            if (string.Equals(kv.Key, MarkupEnabledKey, StringComparison.Ordinal))
            {
                continue;
            }

            placeholders[w++] = new Placeholder(kv.Key, kv.Value, kv.Value?.GetType());
        }

        return (originalFormat, placeholders, allowMarkup);
    }

    private static (string? OriginalFormat, Placeholder[] Placeholders, bool AllowMarkup) ExtractFromEnumerable(IEnumerable<KeyValuePair<string, object?>> enumerable)
    {
        string? originalFormat = null;
        var allowMarkup = false;
        List<Placeholder>? placeholders = null;
        foreach (var kv in enumerable)
        {
            if (string.Equals(kv.Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                originalFormat = kv.Value as string;
                continue;
            }
            if (string.Equals(kv.Key, MarkupEnabledKey, StringComparison.Ordinal))
            {
                allowMarkup = kv.Value is true;
                continue;
            }

            placeholders ??= new List<Placeholder>(4);
            placeholders.Add(new Placeholder(kv.Key, kv.Value, kv.Value?.GetType()));
        }

        return (originalFormat, placeholders?.ToArray() ?? [], allowMarkup);
    }
}
