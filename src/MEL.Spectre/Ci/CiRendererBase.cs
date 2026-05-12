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
        var markup = _context.Formatter.Format(entry, maskValues);

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

        var prefix = BuildLevelAnnotationPrefix(entry.Level);
        if (prefix is not null)
        {
            console.Write(prefix);
        }

        var indent = BuildIndent(scopeDepth);
        if (indent is not null)
        {
            console.Markup(indent);
        }

        console.MarkupLine(markup);

        if (entry.Exception is not null)
        {
            console.WriteException(entry.Exception, _context.ExceptionFormats);
        }
    }

    protected virtual string? BuildLevelAnnotationPrefix(LogLevel level) => null;

    protected virtual string? BuildIndent(int depth) => null;
}
