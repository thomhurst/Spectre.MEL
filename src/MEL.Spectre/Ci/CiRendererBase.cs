using System.Diagnostics;
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

    protected string FormatPlainMessage(LogEntry entry) =>
        Markup.Remove(_context.Formatter.FormatMessage(entry, escapeMessageMarkup: true));

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
        var rawExceptionText = exception.ToString();
        var terminalValues = GetTerminalValues(exceptions, rawExceptionText);
        if (terminalValues.Any(ContainsUnsafeTerminalControl))
        {
            for (var i = 0; i < terminalValues.Count; i++)
            {
                CollectTerminalVisibleMaskValues(terminalValues[i], maskValues);
            }

            return RedactException(exception);
        }
        var normalizedMessages = exceptions
            .Select(current => PlaceholderFormatter.NormalizeForMasking(current.Message, normalizeLineEndings: true))
            .ToArray();
        string exceptionText;
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var renderWidth = (int)Math.Min(
                int.MaxValue,
                (long)rawExceptionText.Length + ExceptionMaskingRenderWidth);
            var renderOptions = RenderOptions.Create(console) with
            {
                ConsoleSize = new Size(renderWidth, console.Profile.Height),
            };
            var builder = new StringBuilder();
            foreach (var segment in exception.GetRenderable(_context.ExceptionFormats).Render(renderOptions, renderWidth))
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

        var maskedException = MaskExceptionMessages(
            normalizedExceptionText,
            normalizedMessages,
            maskValues,
            out var found,
            out var requiresRedaction);
        if (requiresRedaction)
        {
            return RedactException(exception);
        }

        if (_context.Masker.TryMaskValuePatterns(maskedException, maskValues, out var renderedMaskedException))
        {
            maskedException = renderedMaskedException;
            found = true;
        }

        return found ? maskedException : null;
    }

    private string MaskExceptionMessages(
        string exceptionText,
        string[] messages,
        List<string> maskValues,
        out bool found,
        out bool requiresRedaction)
    {
        var ranges = new List<ExceptionMaskRange>();
        requiresRedaction = false;

        for (var i = 0; i < messages.Length; i++)
        {
            var message = messages[i];
            var matchedValues = new List<string>();
            if (!_context.Masker.TryMaskValuePatterns(message, matchedValues, out _))
            {
                continue;
            }

            maskValues.AddRange(matchedValues);
            if (message.Length == 0)
            {
                requiresRedaction = true;
                found = false;
                return exceptionText;
            }

            var occurrence = 0;
            var mappedMessage = false;
            while ((occurrence = exceptionText.IndexOf(message, occurrence, StringComparison.Ordinal)) >= 0)
            {
                mappedMessage = true;
                AddMessageMaskRanges(message, occurrence, matchedValues, ranges);
                occurrence += message.Length;
            }

            if (!mappedMessage)
            {
                requiresRedaction = true;
                found = false;
                return exceptionText;
            }
        }

        found = ranges.Count > 0;
        if (!found)
        {
            return exceptionText;
        }

        ranges.Sort(static (left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            return byStart != 0 ? byStart : right.End.CompareTo(left.End);
        });

        var builder = new StringBuilder(exceptionText.Length);
        var copiedLength = 0;
        var hasMaskedRange = false;
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            if (range.End <= copiedLength)
            {
                continue;
            }
            if (hasMaskedRange && range.Start <= copiedLength)
            {
                copiedLength = range.End;
                continue;
            }

            builder.Append(exceptionText, copiedLength, range.Start - copiedLength);
            builder.Append("***");
            copiedLength = range.End;
            hasMaskedRange = true;
        }
        builder.Append(exceptionText, copiedLength, exceptionText.Length - copiedLength);
        return builder.ToString();
    }

    private static void AddMessageMaskRanges(
        string message,
        int messageOffset,
        List<string> matchedValues,
        List<ExceptionMaskRange> ranges)
    {
        for (var i = 0; i < matchedValues.Count; i++)
        {
            var matchedValue = matchedValues[i];
            if (matchedValue.Length == 0)
            {
                continue;
            }

            var occurrence = 0;
            while ((occurrence = message.IndexOf(matchedValue, occurrence, StringComparison.Ordinal)) >= 0)
            {
                ranges.Add(new ExceptionMaskRange(
                    messageOffset + occurrence,
                    messageOffset + occurrence + matchedValue.Length));
                occurrence += matchedValue.Length;
            }
        }
    }

    private static string RedactException(Exception exception)
    {
        var redactedException = SecretMasker.Mask(exception);
        return RuntimeFeature.IsDynamicCodeSupported
            ? redactedException + Environment.NewLine
            : redactedException;
    }

    private static List<string> GetTerminalValues(Exception[] exceptions, string rawExceptionText)
    {
        var values = new List<string> { rawExceptionText };
        for (var i = 0; i < exceptions.Length; i++)
        {
            var exception = exceptions[i];
            values.Add(exception.Message);
            if (exception.StackTrace is { } stackTrace)
            {
                values.Add(stackTrace);
            }

            var frames = new StackTrace(exception, fNeedFileInfo: true).GetFrames();
            for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                if (frames[frameIndex].GetFileName() is { } fileName)
                {
                    values.Add(fileName);
                }
            }
        }
        return values;
    }

    private void CollectTerminalVisibleMaskValues(string value, List<string> maskValues)
    {
        var terminalText = ReplayTerminalText(value);
        _context.Masker.TryMaskValuePatterns(terminalText, maskValues, out _);
    }

    private static bool ContainsUnsafeTerminalControl(string value) =>
        ContainsBareCarriageReturn(value) || ContainsNonSgrAnsi(value);

    private static string ReplayTerminalText(string value)
    {
        var output = new StringBuilder(value.Length);
        var line = new StringBuilder();
        var cursor = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current == '\n')
            {
                output.Append(line).Append('\n');
                line.Clear();
                cursor = 0;
                continue;
            }
            if (current == '\r')
            {
                cursor = 0;
                continue;
            }
            if (current == '\b')
            {
                cursor = Math.Max(0, cursor - 1);
                continue;
            }
            if (TryReadCsi(value, ref i, out var parameter, out var command))
            {
                if (command == 'P')
                {
                    var deleteCount = Math.Min(parameter, Math.Max(0, line.Length - cursor));
                    if (deleteCount > 0)
                    {
                        line.Remove(cursor, deleteCount);
                    }
                }
                else
                {
                    cursor = command switch
                    {
                        'C' or 'a' => (int)Math.Min(value.Length, (long)cursor + parameter),
                        'D' => Math.Max(0, cursor - parameter),
                        'G' or '`' => Math.Min(value.Length, parameter) - 1,
                        _ => cursor,
                    };
                }
                continue;
            }
            if (char.IsControl(current) && current != '\t')
            {
                continue;
            }

            while (line.Length < cursor)
            {
                line.Append(' ');
            }
            if (cursor < line.Length)
            {
                line[cursor] = current;
            }
            else
            {
                line.Append(current);
            }
            cursor++;
        }

        return output.Append(line).ToString();
    }

    private static bool TryReadCsi(string value, ref int index, out int parameter, out char command)
    {
        var current = value[index];
        var sequenceIndex = index;
        if (current == AnsiSanitizer.EscapeChar)
        {
            if (++sequenceIndex >= value.Length || value[sequenceIndex] != '[')
            {
                parameter = 1;
                command = default;
                return false;
            }
            sequenceIndex++;
        }
        else if (current == AnsiSanitizer.CsiChar)
        {
            sequenceIndex++;
        }
        else
        {
            parameter = 1;
            command = default;
            return false;
        }

        var parameterStart = sequenceIndex;
        while (sequenceIndex < value.Length && value[sequenceIndex] is >= '\x30' and <= '\x3f')
        {
            sequenceIndex++;
        }
        var parameterEnd = sequenceIndex;
        while (sequenceIndex < value.Length && value[sequenceIndex] is >= '\x20' and <= '\x2f')
        {
            sequenceIndex++;
        }
        if (sequenceIndex >= value.Length || value[sequenceIndex] is not (>= '\x40' and <= '\x7e'))
        {
            parameter = 1;
            command = default;
            return false;
        }

        var parameterText = value.AsSpan(parameterStart, parameterEnd - parameterStart);
        var separator = parameterText.IndexOf(';');
        if (separator >= 0)
        {
            parameterText = parameterText[..separator];
        }
        parameter = int.TryParse(parameterText, out var parsed) && parsed > 0 ? parsed : 1;
        command = value[sequenceIndex];
        index = sequenceIndex;
        return true;
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

    private static bool ContainsNonSgrAnsi(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!AnsiSanitizer.IsSequenceIntroducer(value[i]))
            {
                continue;
            }

            var sequenceIndex = i;
            if (value[i] == AnsiSanitizer.EscapeChar)
            {
                if (++sequenceIndex >= value.Length || value[sequenceIndex] != '[')
                {
                    return true;
                }
                sequenceIndex++;
            }
            else if (value[i] == AnsiSanitizer.CsiChar)
            {
                sequenceIndex++;
            }
            else
            {
                return true;
            }

            while (sequenceIndex < value.Length && value[sequenceIndex] is >= '\x30' and <= '\x3f')
            {
                sequenceIndex++;
            }
            while (sequenceIndex < value.Length && value[sequenceIndex] is >= '\x20' and <= '\x2f')
            {
                sequenceIndex++;
            }
            if (sequenceIndex >= value.Length || value[sequenceIndex] != 'm')
            {
                return true;
            }
            i = sequenceIndex;
        }
        return false;
    }

    private readonly record struct ExceptionMaskRange(int Start, int End);

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
