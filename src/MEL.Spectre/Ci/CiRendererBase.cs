using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Rendering;
using MEL.Spectre.Scopes;

namespace MEL.Spectre.Ci;

internal abstract class CiRendererBase : ICiRenderer
{
    private const int ExceptionMaskingRenderWidth = 1_000_000;

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
        RenderEntryCore(console, entry, scopeDepth, escapeMessageMarkup: false);
    }

    public virtual void RenderEntryFallback(IAnsiConsole console, LogEntry entry, int scopeDepth)
    {
        RenderEntryCore(console, entry, scopeDepth, escapeMessageMarkup: true);
    }

    private void RenderEntryCore(IAnsiConsole console, LogEntry entry, int scopeDepth, bool escapeMessageMarkup)
    {
        var maskValues = new List<string>(0);
        var annotation = GetLevelAnnotation(entry.Level);
        var prefix = annotation is null ? null : BuildLevelAnnotationPrefix(annotation.Value);
        var suppressLevel = prefix is not null && _context.SuppressInlineLevelOnCiAnnotation;
        var markup = suppressLevel
            ? _context.Formatter.FormatMessage(entry, maskValues, escapeMessageMarkup)
            : _context.Formatter.Format(entry, maskValues, escapeMessageMarkup);
        var indent = BuildIndent(scopeDepth);
        var renderedMarkup = indent is null ? markup : indent + markup;
        if ((_context.Formatter.AllowsMessageMarkup || entry.AllowMarkup) && !escapeMessageMarkup)
        {
            ValidateMarkup(renderedMarkup);
        }
        var maskedException = MaskException(entry.Exception, console, maskValues);

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

        if (prefix is not null)
        {
            var plainLine = Markup.Remove(renderedMarkup);
            WriteCommand(console, prefix + EscapeLevelAnnotationPayload(plainLine));
        }
        else
        {
            console.MarkupLine(renderedMarkup);
        }

        if (entry.Exception is not null)
        {
            if (maskedException is not null)
            {
                if (RuntimeFeature.IsDynamicCodeSupported)
                {
                    console.Write(new Text(maskedException));
                }
                else
                {
                    console.WriteLine(maskedException);
                }
            }
            else if (RuntimeFeature.IsDynamicCodeSupported)
            {
                console.WriteException(entry.Exception, _context.ExceptionFormats);
            }
            else
            {
                console.MarkupLine(Markup.Escape(FormatExceptionForAot(entry.Exception, _context.ExceptionFormats)));
            }
        }
    }

    protected CiAnnotation? GetLevelAnnotation(LogLevel level) => _context.LevelAnnotations.Get(level);

    protected virtual string? BuildLevelAnnotationPrefix(CiAnnotation annotation) => null;

    protected virtual string EscapeLevelAnnotationPayload(string payload) => payload;

    protected virtual string? BuildIndent(int depth) => null;

    protected static void WriteCommand(IAnsiConsole console, string command) =>
        console.Profile.Out.Writer.WriteLine(command);

    internal static string FormatExceptionForAot(Exception exception, ExceptionFormats formats)
    {
        if ((formats & ExceptionFormats.NoStackTrace) == 0)
        {
            return exception.ToString();
        }

        var builder = new StringBuilder();
        AppendExceptionWithoutStack(builder, exception);
        return builder.ToString();
    }

    private static void AppendExceptionWithoutStack(StringBuilder builder, Exception exception)
    {
        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.Append(" ---> ");
        }

        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        builder.Append(typeName).Append(": ").Append(exception.Message);

        if (exception is AggregateException aggregate)
        {
            foreach (var innerException in aggregate.InnerExceptions)
            {
                AppendExceptionWithoutStack(builder, innerException);
            }
        }
        else if (exception.InnerException is not null)
        {
            AppendExceptionWithoutStack(builder, exception.InnerException);
        }
    }

    private static void ValidateMarkup(string markup)
    {
        try
        {
            _ = new Markup(markup);
        }
        catch (InvalidOperationException ex)
        {
            throw new MalformedMarkupException(ex);
        }
    }

    private string? MaskException(Exception? exception, IAnsiConsole console, List<string> maskValues)
    {
        if (exception is null
            || !_context.Masker.HasValuePatterns)
        {
            return null;
        }

        var exceptions = EnumerateExceptions(exception).ToArray();
        if (exceptions.Any(current => ContainsBareCarriageReturn(current.Message)))
        {
            var redactedException = SecretMasker.Mask(exception);
            return RuntimeFeature.IsDynamicCodeSupported
                ? redactedException + Environment.NewLine
                : redactedException;
        }
        var normalizedMessages = exceptions
            .Select(current => PlaceholderFormatter.NormalizeForMasking(current.Message, normalizeLineEndings: true))
            .ToArray();
        string exceptionText;
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var renderOptions = RenderOptions.Create(console) with
            {
                ConsoleSize = new Size(ExceptionMaskingRenderWidth, console.Profile.Height),
            };
            var builder = new StringBuilder();
            foreach (var segment in exception.GetRenderable(_context.ExceptionFormats).Render(renderOptions, ExceptionMaskingRenderWidth))
            {
                builder.Append(segment.Text);
            }
            exceptionText = builder.ToString();
        }
        else
        {
            exceptionText = FormatExceptionForAot(exception, _context.ExceptionFormats);
        }
        var normalizedExceptionText = PlaceholderFormatter.NormalizeForMasking(exceptionText, normalizeLineEndings: true);

        if (!_context.Masker.ShouldMaskValue(normalizedExceptionText)
            && !normalizedMessages.Any(_context.Masker.ShouldMaskValue))
        {
            return null;
        }

        var found = _context.Masker.TryMaskValuePatterns(normalizedExceptionText, maskValues, out var maskedException);

        for (var i = 0; i < exceptions.Length; i++)
        {
            if (_context.Masker.TryMaskValuePatterns(normalizedMessages[i], maskValues, out var maskedMessage))
            {
                maskedException = normalizedMessages[i].Length == 0
                    ? maskedMessage
                    : maskedException.Replace(normalizedMessages[i], maskedMessage, StringComparison.Ordinal);
                found = true;
            }
        }

        return found ? maskedException : null;
    }

    private static bool ContainsBareCarriageReturn(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\r' && (i + 1 == value.Length || value[i + 1] != '\n'))
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception root)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(root);

        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;
            if (current is AggregateException aggregate)
            {
                for (var i = aggregate.InnerExceptions.Count - 1; i >= 0; i--)
                {
                    pending.Push(aggregate.InnerExceptions[i]);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }
    }
}
