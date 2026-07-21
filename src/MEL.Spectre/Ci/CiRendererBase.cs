using Microsoft.Extensions.Logging;
using Spectre.Console;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Rendering;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Ci;

internal abstract class CiRendererBase : ICiRenderer
{
    private readonly RendererContext _context;

    protected CiRendererBase(RendererContext context)
    {
        _context = context;
    }

    public abstract string Name { get; }

    public abstract CiCapabilities Capabilities { get; }

    public virtual void EmitMask(IAnsiConsole console, string value)
    {
    }

    public virtual void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
    }

    public virtual void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth)
    {
    }

    public virtual void RenderEntry(IAnsiConsole console, LogEntry entry, int scopeDepth)
    {
        var maskValues = new List<string>(0);
        var annotation = GetLevelAnnotation(entry.Level);
        var prefix = annotation is null ? null : BuildLevelAnnotationPrefix(annotation.Value);
        var suppressLevel = prefix is not null && _context.SuppressInlineLevelOnCiAnnotation;
        var markup = _context.Formatter.Format(entry, maskValues, suppressLevel);

        if (Capabilities.SupportsMasking)
        {
            for (var i = 0; i < maskValues.Count; i++)
            {
                if (_context.Masker.TryRegisterForEmission(maskValues[i]))
                {
                    EmitMask(console, maskValues[i]);
                }
            }
        }

        var indent = BuildIndent(scopeDepth);
        if (prefix is not null)
        {
            var plainLine = Markup.Remove(indent is null ? markup : indent + markup);
            WriteCommand(console, prefix + EscapeLevelAnnotationPayload(plainLine));
        }
        else
        {
            if (indent is not null)
            {
                console.Markup(indent);
            }

            console.MarkupLine(markup);
        }

        if (entry.Exception is not null)
        {
            console.WriteException(entry.Exception, _context.ExceptionFormats);
        }
    }

    protected CiAnnotation? GetLevelAnnotation(LogLevel level) => _context.LevelAnnotations.Get(level);

    protected virtual string? BuildLevelAnnotationPrefix(CiAnnotation annotation) => null;

    protected virtual string EscapeLevelAnnotationPayload(string payload) => payload;

    protected virtual string? BuildIndent(int depth) => null;

    protected static void WriteCommand(IAnsiConsole console, string command) =>
        console.Profile.Out.Writer.WriteLine(command);
}
