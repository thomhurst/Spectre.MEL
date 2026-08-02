namespace MEL.Spectre.Provider;

internal static class StateReader
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    public static (string? OriginalFormat, Placeholder[] Placeholders, bool AllowMarkup) Extract<TState>(TState state)
    {
        if (state is IMarkupLogState)
        {
            return (null, [], true);
        }

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
        var count = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var key = list[i].Key;
            if (string.Equals(key, OriginalFormatKey, StringComparison.Ordinal))
            {
                originalFormat = list[i].Value as string;
            }
            else
            {
                count++;
            }
        }

        if (count == 0)
        {
            return (originalFormat, [], false);
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
            placeholders[w++] = new Placeholder(kv.Key, kv.Value, kv.Value?.GetType());
        }

        return (originalFormat, placeholders, false);
    }

    private static (string? OriginalFormat, Placeholder[] Placeholders, bool AllowMarkup) ExtractFromEnumerable(IEnumerable<KeyValuePair<string, object?>> enumerable)
    {
        string? originalFormat = null;
        List<Placeholder>? placeholders = null;
        foreach (var kv in enumerable)
        {
            if (string.Equals(kv.Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                originalFormat = kv.Value as string;
                continue;
            }
            placeholders ??= new List<Placeholder>(4);
            placeholders.Add(new Placeholder(kv.Key, kv.Value, kv.Value?.GetType()));
        }

        return (originalFormat, placeholders?.ToArray() ?? [], false);
    }
}

internal interface IMarkupLogState
{
}
