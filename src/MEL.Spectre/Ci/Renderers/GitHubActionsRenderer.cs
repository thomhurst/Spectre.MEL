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

        if (remaining.IndexOfAny('\r', '\n') >= 0)
        {
            WriteMask(writer, remaining);
        }

        while (!remaining.IsEmpty)
        {
            var newlineIndex = remaining.IndexOfAny('\r', '\n');
            var line = newlineIndex >= 0 ? remaining[..newlineIndex] : remaining;
            if (line.Length > MaskChunkLength)
            {
                WriteMask(writer, line);
            }
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
        while (value.Length > MaskChunkLength)
        {
            var chunkLength = MaskChunkLength;
            if (char.IsHighSurrogate(value[chunkLength - 1])
                && char.IsLowSurrogate(value[chunkLength]))
            {
                chunkLength--;
            }

            WriteMask(writer, value[..chunkLength]);

            if (value.Length - chunkLength < MaskChunkLength)
            {
                var finalStart = value.Length - MaskChunkLength;
                if (finalStart > 0
                    && char.IsLowSurrogate(value[finalStart])
                    && char.IsHighSurrogate(value[finalStart - 1]))
                {
                    finalStart--;
                }

                WriteMask(writer, value[finalStart..]);
                return;
            }

            value = value[chunkLength..];
        }

        if (!value.IsEmpty)
        {
            WriteMask(writer, value);
        }
    }

    private static void WriteMask(TextWriter writer, ReadOnlySpan<char> value)
    {
        writer.Write(AddMaskPrefix);
        WriteWorkflowCommandValue(writer, value);
    }

    private static void WriteWorkflowCommandValue(TextWriter writer, ReadOnlySpan<char> value)
    {
        while (true)
        {
            var escapeIndex = value.IndexOfAny('%', '\r', '\n');
            if (escapeIndex < 0)
            {
                writer.WriteLine(value);
                return;
            }

            writer.Write(value[..escapeIndex]);
            writer.Write(value[escapeIndex] switch
            {
                '%' => "%25",
                '\r' => "%0D",
                _ => "%0A",
            });
            value = value[(escapeIndex + 1)..];
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
