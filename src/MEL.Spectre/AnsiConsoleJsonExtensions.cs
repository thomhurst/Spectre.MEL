using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Spectre.Console;
using MEL.Spectre.Ci;

namespace MEL.Spectre;

public static class AnsiConsoleJsonExtensions
{
    private const string GitLabClearLine = "\r\x1b[0K";

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes <paramref name="payload"/> as indented JSON inside a Spectre.Console panel with the given
    /// <paramref name="title"/>. When running under a CI that supports grouping (GitHub Actions, Azure
    /// Pipelines, GitLab CI), the panel is also wrapped in a collapsible group so it doesn't dominate
    /// the log scroll.
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use WriteJsonPanelTrimSafe with JsonTypeInfo<T> for trim-safe serialization.")]
    [RequiresDynamicCode("JSON serialization may require runtime code generation. Use WriteJsonPanelTrimSafe with JsonTypeInfo<T> for NativeAOT-safe serialization.")]
    public static void WriteJsonPanel(this IAnsiConsole console, string title, object? payload, CiMode? ciMode = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(title);

        var json = payload is null
            ? "null"
            : JsonSerializer.Serialize(payload, IndentedJson);
        WriteJsonPanelCore(console, title, json, ciMode);
    }

    /// <summary>
    /// Writes <paramref name="payload"/> as JSON using source-generated metadata inside a
    /// Spectre.Console panel with the given <paramref name="title"/>.
    /// </summary>
    public static void WriteJsonPanelTrimSafe<T>(this IAnsiConsole console, string title, T payload, JsonTypeInfo<T> jsonTypeInfo, CiMode? ciMode = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var json = JsonSerializer.Serialize(payload, jsonTypeInfo);
        WriteJsonPanelCore(console, title, json, ciMode);
    }

    private static void WriteJsonPanelCore(IAnsiConsole console, string title, string json, CiMode? ciMode)
    {
        var resolvedMode = ciMode ?? CiDetector.DetectFromEnvironment();
        var groupOpen = ResolveGroupOpen(resolvedMode, title);
        var groupClose = ResolveGroupClose(resolvedMode, title);

        if (groupOpen is not null)
        {
            console.WriteLine(groupOpen);
        }

        var panel = new Panel(new Markup(Markup.Escape(json)))
        {
            Header = new PanelHeader($" {title} "),
            Border = BoxBorder.Rounded,
            Expand = false,
        };
        console.Write(panel);

        if (groupClose is not null)
        {
            console.WriteLine(groupClose);
        }
    }

    private static string? ResolveGroupOpen(CiMode mode, string title) => mode switch
    {
        CiMode.GitHubActions => $"::group::{title}",
        CiMode.AzurePipelines => $"##[group]{title}",
        CiMode.GitLabCi => $"section_start:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:json_{Slug(title)}[collapsed=true]{GitLabClearLine}{title}",
        _ => null,
    };

    private static string? ResolveGroupClose(CiMode mode, string title) => mode switch
    {
        CiMode.GitHubActions => "::endgroup::",
        CiMode.AzurePipelines => "##[endgroup]",
        CiMode.GitLabCi => $"section_end:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:json_{Slug(title)}{GitLabClearLine}",
        _ => null,
    };

    private static string Slug(string s)
    {
        var chars = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            chars[i] = char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_';
        }
        return new string(chars);
    }
}
