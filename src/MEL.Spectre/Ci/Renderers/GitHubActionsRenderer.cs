using Microsoft.Extensions.Logging;
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
        console.WriteLine($"::add-mask::{value}");
    }

    public override void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine($"::group::{frame.Label}");
    }

    public override void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine("::endgroup::");
    }

    protected override string? BuildLevelAnnotationPrefix(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => "::error::",
        LogLevel.Warning => "::warning::",
        LogLevel.Debug or LogLevel.Trace => "::debug::",
        _ => null,
    };
}
