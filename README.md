# Spectre.MEL

A `Microsoft.Extensions.Logging` provider that renders log entries through
[Spectre.Console](https://github.com/spectreconsole/spectre.console) with
first-class awareness of CI runners.

- Rich ANSI colour, exception rendering, scope handling.
- Type-aware placeholder highlighting (`int`→cyan, `string`→yellow, ...) with
  name-hint overrides (`UserId`, `Email`, `StatusCode`, ...).
- Secret masking by regex on placeholder names (`password`, `token`, `secret`,
  `apikey`, `bearer`, ...).
- Interactive vs non-interactive detection with sensible ANSI behaviour.
- Channel-based background writer, single consumer, ordered output.
- Works with `[LoggerMessage]` source-generated logging.

## CI runner support

Auto-detected from environment variables. Runners with **native renderers**
emit collapsible groups, level annotations, and (where supported) secret masks:

| Runner | Group syntax | Level annotations | Secret mask |
|--------|--------------|-------------------|-------------|
| GitHub Actions | `::group::` / `::endgroup::` | `::error::` / `::warning::` / `::debug::` | `::add-mask::` |
| Azure Pipelines | `##[group]` / `##[endgroup]` | `##[error]` / `##[warning]` / `##[debug]` | — |
| GitLab CI | `section_start` / `section_end` | — | — |
| TeamCity | `##teamcity[blockOpened]` | `##teamcity[message status=...]` | — |
| Buildkite | `--- <label>` | — | — |
| Travis | `travis_fold:start/end` | — | — |

Jenkins, CircleCI, and AppVeyor are detected and use a passthrough renderer:
plain ANSI output with no grouping or annotations.

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

`AddSpectreConsole` removes the registered `ConsoleLoggerProvider` so you do
not get duplicate output.

## Themes

```csharp
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.MEL;
using Spectre.MEL.Theme;

builder.AddSpectreConsole(o =>
{
    o.Theme = SpectreTheme.Dark
        .ForLevel(LogLevel.Information, new Style(Color.Green))
        .WithPlaceholders(p =>
        {
            p.ForName("UserId", Color.Aqua);
            p.ForType<bool>(Color.Magenta1);
        });
    o.Theme.MessageStyle = new Style(Color.White);
});
```

Built-in themes: `Default`, `Dark`, `Light`, `Monochrome`.

> Both `SpectreTheme` and its `PlaceholderStyleResolver` are **configure-once**:
> they freeze when the provider is constructed. Mutating styles or adding rules
> afterwards throws `InvalidOperationException` from the setter / fluent call.
> Invalid regex patterns, malformed templates, and out-of-range timeouts all
> fail validation at host startup via `IValidateOptions<SpectreConsoleLoggerOptions>`
> (chained with `.ValidateOnStart()`).

## CI detection

```csharp
builder.AddSpectreConsole(o =>
{
    o.CiMode = CiMode.GitHubActions; // or Auto, Off, AzurePipelines, etc.
});
```

## Secret masking

Placeholders whose name matches any of the configured regex patterns are
rendered as `***`. On GitHub Actions, Spectre.MEL also emits `::add-mask::`
once per distinct value so the unmasked value is redacted from subsequent
build steps.

```csharp
builder.AddSpectreConsole(o =>
{
    o.MaskedNamePatterns.Add("session.*id");
});
```

> `MaskedNamePatterns` is snapshotted at provider construction; mutations
> after the provider starts are ignored.

## Backpressure

The background writer uses a bounded `Channel<LogEntry>`. When full:

- `BackpressureMode.Wait` (default) — log call spins, then waits up to
  `EnqueueWaitTimeout` (default 1 s, must be > 0 and ≤ `ShutdownDrainTimeout`)
  before dropping with a counter increment.
- `BackpressureMode.DropNewest` — drop the incoming entry.
- `BackpressureMode.DropOldest` — drop the oldest queued entry.

Drops (backpressure or post-disposal) each emit a one-shot warning to
`stderr`, falling back to `Debug.WriteLine` if `stderr` is unavailable.

## License

MIT
