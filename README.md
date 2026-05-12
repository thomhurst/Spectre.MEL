# Spectre.MEL

A `Microsoft.Extensions.Logging` provider that renders log entries through
[Spectre.Console](https://github.com/spectreconsole/spectre.console) with
first-class awareness of CI runners.

- Rich ANSI colour, exception rendering, scope handling.
- Native CI integration: GitHub Actions, Azure Pipelines, GitLab CI, TeamCity,
  Jenkins, CircleCI, Buildkite, AppVeyor, Travis. Scopes become collapsible
  groups, warnings/errors get native annotations, secrets get `::add-mask::`.
- Type-aware placeholder highlighting (`int`→cyan, `string`→yellow, ...) with
  name-hint overrides (`UserId`, `Email`, `StatusCode`, ...).
- Secret masking by regex on placeholder names (`password`, `token`, `secret`,
  `apikey`, `bearer`, ...).
- Interactive vs non-interactive detection with sensible ANSI behaviour.
- Channel-based background writer, single consumer, ordered output.
- Works with `[LoggerMessage]` source-generated logging.

## Install

```sh
dotnet add package Spectre.MEL
```

## Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.MEL;

var services = new ServiceCollection()
    .AddLogging(builder => builder.AddSpectreConsole());

var sp = services.BuildServiceProvider();
var logger = sp.GetRequiredService<ILogger<Program>>();
logger.LogInformation("User {UserId} logged in", 42);
```

`AddSpectreConsole` removes the default `ConsoleLoggerProvider` so you do not
get duplicate output. Pass `o => o.ReplaceDefaultConsoleLogger = false` to
disable that behaviour.

## Themes

```csharp
builder.AddSpectreConsole(o =>
{
    o.Theme = SpectreTheme.Dark
        .ForLevel(LogLevel.Information, new Style(Color.Green))
        .WithPlaceholders(p =>
        {
            p.ForName("UserId", Color.Aqua);
            p.ForType<bool>(Color.Magenta1);
        });
});
```

Built-in themes: `Default`, `Dark`, `Light`, `Monochrome`.

## CI detection

By default Spectre.MEL auto-detects the active CI runner from environment
variables (`GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`, ...). Override with
`o.CiMode = CiMode.GitHubActions` or `CiMode.Off`.

## Secret masking

Placeholders whose name matches any of the configured regex patterns are
rendered as `***`. On GitHub Actions, Spectre.MEL also emits `::add-mask::` so
the unmasked value is redacted from subsequent build steps.

```csharp
builder.AddSpectreConsole(o =>
{
    o.MaskedNamePatterns.Add("(?i)session.*id");
});
```

## License

MIT
