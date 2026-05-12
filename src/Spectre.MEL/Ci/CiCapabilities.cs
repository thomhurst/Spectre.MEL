namespace Spectre.MEL.Ci;

internal sealed record CiCapabilities(
    bool SupportsGrouping,
    bool SupportsAnsi,
    bool SupportsLevelAnnotations,
    bool SupportsMasking)
{
    public static readonly CiCapabilities PlainTty = new(false, true, false, false);
}
