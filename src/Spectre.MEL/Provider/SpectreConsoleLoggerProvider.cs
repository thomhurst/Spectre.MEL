using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.MEL.Ci;
using Spectre.MEL.Ci.Renderers;
using Spectre.MEL.Masking;
using Spectre.MEL.Rendering;
using Spectre.MEL.Templates;

namespace Spectre.MEL.Provider;

[ProviderAlias("SpectreConsole")]
internal sealed class SpectreConsoleLoggerProvider : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SpectreConsoleLogger> _loggers = new(StringComparer.Ordinal);
    private readonly BackgroundWriter _writer;
    private readonly SpectreConsoleLoggerOptions _options;
    private IExternalScopeProvider? _scopeProvider;
    private bool _disposed;

    public SpectreConsoleLoggerProvider(IOptions<SpectreConsoleLoggerOptions> options)
    {
        _options = options.Value;

        var ciMode = _options.CiMode == CiMode.Auto ? CiDetector.DetectFromEnvironment() : _options.CiMode;
        var console = _options.Console ?? BuildAnsiConsole(_options, ciMode);
        var template = new OutputTemplate(_options.Template);
        var masker = new SecretMasker(_options.MaskedNamePatterns, _options.MaskedValueCacheCapacity);
        var formatter = new EntryFormatter(template, _options.Theme, masker);
        var context = new RendererContext(formatter, masker, _options.ExceptionFormats);
        var renderer = ResolveRenderer(ciMode, context);

        _writer = new BackgroundWriter(
            console,
            renderer,
            _options.ChannelCapacity,
            _options.BackpressureMode,
            _options.ShutdownDrainTimeout);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name =>
            new SpectreConsoleLogger(
                name,
                _writer,
                () => _scopeProvider,
                _options.IncludeScopes,
                _options.IncludeActivity));
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _writer.DisposeAsync().ConfigureAwait(false);
    }

    private static IAnsiConsole BuildAnsiConsole(SpectreConsoleLoggerOptions options, CiMode ciMode)
    {
        var isInteractive = options.InteractivityMode switch
        {
            InteractivityMode.Interactive => true,
            InteractivityMode.NonInteractive => false,
            _ => TtyDetector.IsInteractiveTty(),
        };

        var inCi = ciMode != CiMode.Off;

        var settings = new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(System.Console.Out),
            Interactive = isInteractive ? InteractionSupport.Yes : InteractionSupport.No,
            Ansi = (isInteractive || inCi) ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = ColorSystemSupport.Detect,
        };

        var console = AnsiConsole.Create(settings);
        if (!isInteractive)
        {
            console.Profile.Width = 1_000_000;
        }
        return console;
    }

    private static ICiRenderer ResolveRenderer(CiMode mode, RendererContext context) => mode switch
    {
        CiMode.GitHubActions => new GitHubActionsRenderer(context),
        CiMode.AzurePipelines => new AzurePipelinesRenderer(context),
        CiMode.GitLabCi => new GitLabCiRenderer(context),
        CiMode.TeamCity => new TeamCityRenderer(context),
        CiMode.Buildkite => new BuildkiteRenderer(context),
        CiMode.Travis => new TravisRenderer(context),
        CiMode.Jenkins => new PassthroughCiRenderer("Jenkins", PassthroughCapabilities, context),
        CiMode.CircleCi => new PassthroughCiRenderer("CircleCi", PassthroughCapabilities, context),
        CiMode.AppVeyor => new PassthroughCiRenderer("AppVeyor", PassthroughCapabilities, context),
        _ => new PlainTtyRenderer(context),
    };

    private static readonly CiCapabilities PassthroughCapabilities = new(SupportsGrouping: false, SupportsAnsi: true, SupportsLevelAnnotations: false, SupportsMasking: false);
}
