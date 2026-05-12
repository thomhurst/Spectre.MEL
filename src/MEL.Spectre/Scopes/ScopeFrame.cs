namespace MEL.Spectre.Scopes;

internal readonly record struct ScopeFrame(long Id, string Label, IReadOnlyList<KeyValuePair<string, object?>>? Properties);
