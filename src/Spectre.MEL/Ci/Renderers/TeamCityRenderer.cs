using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.MEL.Provider;
using Spectre.MEL.Scopes;

namespace Spectre.MEL.Ci.Renderers;

internal sealed class TeamCityRenderer : CiRendererBase
{
    public TeamCityRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "TeamCity";

    public override CiCapabilities Capabilities { get; } = new(SupportsGrouping: true, SupportsAnsi: true, SupportsLevelAnnotations: true, SupportsMasking: false);

    public override void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine($"##teamcity[blockOpened name='{Escape(frame.Label)}']");
    }

    public override void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine($"##teamcity[blockClosed name='{Escape(frame.Label)}']");
    }

    public override void RenderEntry(IAnsiConsole console, LogEntry entry, int scopeDepth)
    {
        if (entry.Level >= LogLevel.Warning)
        {
            var status = entry.Level >= LogLevel.Error ? "ERROR" : "WARNING";
            console.WriteLine($"##teamcity[message text='{Escape(entry.Message)}' status='{status}']");
        }
        base.RenderEntry(console, entry, scopeDepth);
    }

    private static string Escape(string text) => text
        .Replace("|", "||", StringComparison.Ordinal)
        .Replace("'", "|'", StringComparison.Ordinal)
        .Replace("\n", "|n", StringComparison.Ordinal)
        .Replace("\r", "|r", StringComparison.Ordinal)
        .Replace("[", "|[", StringComparison.Ordinal)
        .Replace("]", "|]", StringComparison.Ordinal);
}
