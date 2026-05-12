using Spectre.Console;
using Spectre.MEL.Scopes;

namespace Spectre.MEL.Ci.Renderers;

internal sealed class TravisRenderer : CiRendererBase
{
    public TravisRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "Travis";

    public override CiCapabilities Capabilities { get; } = new(SupportsGrouping: true, SupportsAnsi: true, SupportsLevelAnnotations: false, SupportsMasking: false);

    public override void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine($"travis_fold:start:scope_{frame.Id}\r{frame.Label}");
    }

    public override void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine($"travis_fold:end:scope_{frame.Id}\r");
    }
}
