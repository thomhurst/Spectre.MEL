using Spectre.Console;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Ci.Renderers;

internal sealed class GitHubActionsRenderer : CiRendererBase
{
    public GitHubActionsRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "GitHubActions";

    public override CiCapabilities Capabilities { get; } = new(SupportsGrouping: true, SupportsAnsi: true, SupportsLevelAnnotations: true, SupportsMasking: true);

    public override void EmitMask(IAnsiConsole console, string value)
    {
        WriteCommand(console, $"::add-mask::{value}");
    }

    public override void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        WriteCommand(console, $"::group::{frame.Label}");
    }

    public override void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        WriteCommand(console, "::endgroup::");
    }

    protected override string? BuildLevelAnnotationPrefix(CiAnnotation annotation) => annotation switch
    {
        CiAnnotation.Error => "::error::",
        CiAnnotation.Warning => "::warning::",
        CiAnnotation.Debug => "::debug::",
        _ => null,
    };
}
