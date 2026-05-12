using Spectre.Console;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Ci.Renderers;

internal sealed class BuildkiteRenderer : CiRendererBase
{
    public BuildkiteRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "Buildkite";

    public override CiCapabilities Capabilities { get; } = new(SupportsGrouping: true, SupportsAnsi: true, SupportsLevelAnnotations: false, SupportsMasking: false);

    public override void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine($"--- {frame.Label}");
    }
}
