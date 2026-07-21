using Spectre.Console;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Ci.Renderers;

internal sealed class GitHubActionsRenderer : CiRendererBase
{
    private const string AddMaskPrefix = "::add-mask::";
    private const int MaskChunkLength = 1_000;

    public GitHubActionsRenderer(RendererContext context) : base(context)
    {
    }

    public override string Name => "GitHubActions";

    public override CiCapabilities Capabilities { get; } = new(SupportsGrouping: true, SupportsAnsi: true, SupportsLevelAnnotations: true, SupportsMasking: true);

    public override void EmitMask(IAnsiConsole console, string value)
    {
        var writer = console.Profile.Out.Writer;
        var remaining = value.AsSpan();

        if (remaining.IsEmpty)
        {
            writer.WriteLine(AddMaskPrefix);
            return;
        }

        while (!remaining.IsEmpty)
        {
            var newlineIndex = remaining.IndexOfAny('\r', '\n');
            var line = newlineIndex >= 0 ? remaining[..newlineIndex] : remaining;
            WriteMaskChunks(writer, line);

            if (newlineIndex < 0)
            {
                break;
            }

            var newlineLength = remaining[newlineIndex] == '\r'
                && newlineIndex + 1 < remaining.Length
                && remaining[newlineIndex + 1] == '\n'
                    ? 2
                    : 1;
            remaining = remaining[(newlineIndex + newlineLength)..];
        }
    }

    private static void WriteMaskChunks(TextWriter writer, ReadOnlySpan<char> value)
    {
        while (!value.IsEmpty)
        {
            var chunkLength = Math.Min(MaskChunkLength, value.Length);
            if (chunkLength < value.Length
                && char.IsHighSurrogate(value[chunkLength - 1])
                && char.IsLowSurrogate(value[chunkLength]))
            {
                chunkLength--;
            }

            writer.Write(AddMaskPrefix);
            WriteWorkflowCommandValue(writer, value[..chunkLength]);
            value = value[chunkLength..];
        }
    }

    private static void WriteWorkflowCommandValue(TextWriter writer, ReadOnlySpan<char> value)
    {
        while (true)
        {
            var percentIndex = value.IndexOf('%');
            if (percentIndex < 0)
            {
                writer.WriteLine(value);
                return;
            }

            writer.Write(value[..percentIndex]);
            writer.Write("%25");
            value = value[(percentIndex + 1)..];
        }
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
