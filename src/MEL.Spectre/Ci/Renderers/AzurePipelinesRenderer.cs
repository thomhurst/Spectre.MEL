using Microsoft.Extensions.Logging;
using Spectre.Console;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Ci.Renderers;

internal sealed class AzurePipelinesRenderer : CiRendererBase
{
    public AzurePipelinesRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "AzurePipelines";

    public override CiCapabilities Capabilities { get; } = new(SupportsGrouping: true, SupportsAnsi: true, SupportsLevelAnnotations: true, SupportsMasking: false);

    public override void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine($"##[group]{frame.Label}");
    }

    public override void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
        console.WriteLine("##[endgroup]");
    }

    protected override string? BuildLevelAnnotationPrefix(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => "##[error]",
        LogLevel.Warning => "##[warning]",
        LogLevel.Debug or LogLevel.Trace => "##[debug]",
        _ => null,
    };
}
