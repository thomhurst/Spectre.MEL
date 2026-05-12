using Spectre.Console;
using Spectre.MEL.Scopes;

namespace Spectre.MEL.Ci.Renderers;

internal sealed class GitLabCiRenderer : CiRendererBase
{
    private const string ClearLine = "\r\x1b[0K";

    public GitLabCiRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "GitLabCi";

    public override CiCapabilities Capabilities { get; } = new(SupportsGrouping: true, SupportsAnsi: true, SupportsLevelAnnotations: false, SupportsMasking: false);

    public override void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        console.WriteLine($"section_start:{ts}:scope_{frame.Id}[collapsed=true]{ClearLine}{frame.Label}");
    }

    public override void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        console.WriteLine($"section_end:{ts}:scope_{frame.Id}{ClearLine}");
    }
}
