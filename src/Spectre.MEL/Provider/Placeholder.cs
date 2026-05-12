namespace Spectre.MEL.Provider;

internal readonly record struct Placeholder(string Name, object? Value, Type? ValueType);
