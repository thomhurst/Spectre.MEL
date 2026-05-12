namespace Spectre.MEL.Ci.Renderers;

internal sealed class PassthroughCiRenderer : CiRendererBase
{
    public PassthroughCiRenderer(string name, CiCapabilities capabilities, RendererContext context) : base(context)
    {
        Name = name;
        Capabilities = capabilities;
    }

    public override string Name { get; }

    public override CiCapabilities Capabilities { get; }
}
