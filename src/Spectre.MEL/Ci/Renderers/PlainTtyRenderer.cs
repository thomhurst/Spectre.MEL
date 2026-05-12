namespace Spectre.MEL.Ci.Renderers;

internal sealed class PlainTtyRenderer : CiRendererBase
{
    public PlainTtyRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "PlainTty";

    public override CiCapabilities Capabilities => CiCapabilities.PlainTty;

    protected override string? BuildIndent(int depth) => depth > 0 ? new string(' ', depth * 2) : null;
}
