namespace MEL.Spectre.Provider;

internal sealed class MalformedMarkupException(Exception innerException)
    : Exception("Log entry contains malformed Spectre markup.", innerException);
