# MEL.Spectre

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
| GitHub Actions | `::group::` / `::endgroup::` | `::error::` / `::warning::` (`::debug::` opt-in) | `::add-mask::` |
| Azure Pipelines | `##[group]` / `##[endgroup]` | `##[error]` / `##[warning]` (`##[debug]` opt-in) | — |
| GitLab CI | `section_start` / `section_end` | — | — |
| TeamCity | `##teamcity[blockOpened]` | `##teamcity[message status=...]` | — |
| Buildkite | `--- <label>` | — | — |
| Travis | `travis_fold:start/end` | — | — |

Jenkins, CircleCI, and AppVeyor are detected and use a passthrough renderer:
plain ANSI output with no grouping or annotations.

## Install

```sh
dotnet add package MEL.Spectre
```

## Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MEL.Spectre;

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
using MEL.Spectre;
using MEL.Spectre.Theme;

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

CI log entries render without wrapping by default, even when a consumer-supplied
console has a narrow profile. Set `WrapInCi = true` to follow that console width.

Debug and trace entries are ordinary visible log lines by default. Native debug
annotations are often hidden by CI runners; for example, GitHub Actions hides
`::debug::` unless `ACTIONS_STEP_DEBUG=true`. Consumers who enable that runner
setting can opt in explicitly:

```csharp
builder.AddSpectreConsole(o =>
{
    o.CiLevelAnnotations[LogLevel.Debug] = CiAnnotation.Debug;
    o.CiLevelAnnotations[LogLevel.Trace] = CiAnnotation.Debug;
});
```

Native annotation payloads are rendered as plain, message-only text by default,
so the runner supplies severity without retaining level labels or separators
from the full output template. Set `SuppressInlineLevelOnCiAnnotation = false`
to keep the complete template. GitHub Actions annotation payloads also escape
percent signs and embedded newlines so each entry remains one complete workflow
command.

## Secret masking

Placeholders whose name matches any of the configured regex patterns are
rendered as `***`. On GitHub Actions, MEL.Spectre also emits `::add-mask::`
once per distinct value so the unmasked value is redacted from subsequent
build steps. Placeholder values also use mutable `MaskedValuePatterns`
defaults for well-known GitHub, GitLab, AWS, Slack, JWT, and private-key
formats; matching runs only for placeholder string values.

```csharp
builder.AddSpectreConsole(o =>
{
    o.MaskedNamePatterns.Add("session.*id");
    o.MaskedValuePatterns.Add(@"^Bearer\s+\S+");
});
```

> Both pattern lists are snapshotted at provider construction; mutations
> after the provider starts are ignored. Clear either mutable list during
> configuration to disable its defaults.

## Embedded ANSI from child processes

Relayed stdout from tools that style their own output (`dotnet`, `npm`, ...)
often contains raw ANSI escape sequences. An embedded reset (`ESC[0m`) would
terminate the logger's own styling mid-line, leaving the rest of the entry
unstyled. By default MEL.Spectre converts embedded SGR color/style sequences
into Spectre markup: the child process's colors are preserved, an embedded
reset closes only the child's style, and the theme's outer style resumes
afterwards. All other control sequences (cursor movement, screen clearing,
OSC titles and hyperlinks, ...) are removed, which also keeps native CI
annotation payloads (e.g. `::error::`) free of control codes.

```csharp
builder.AddSpectreConsole(o => o.EmbeddedAnsi = EmbeddedAnsiMode.Strip);        // discard child styling entirely
builder.AddSpectreConsole(o => o.EmbeddedAnsi = EmbeddedAnsiMode.Passthrough);  // raw sequences, previous behaviour
```

Sanitization only engages for message content that actually contains an
escape character; plain messages (including multiline `\r\n` text) pass
through unchanged.

## Backpressure

The background writer uses a bounded `Channel<LogEntry>`. When full:

- `BackpressureMode.Wait` (default) — log call spins, then waits up to
  `EnqueueWaitTimeout` (default 1 s, must be > 0 and ≤ `ShutdownDrainTimeout`)
  before dropping with a counter increment.
- `BackpressureMode.DropNewest` — drop the incoming entry.
- `BackpressureMode.DropOldest` — drop the oldest queued entry.

Drops (backpressure or post-disposal) each emit a one-shot warning to
`stderr`, falling back to `Debug.WriteLine` if `stderr` is unavailable.

## Mixing direct console output with logging

Background logging preserves log-entry order but returns from `ILogger.Log`
before rendering. Flush queued entries before an ordering-sensitive direct
write, and take the shared gate so direct output cannot tear a rendered line:

```csharp
var control = services.GetRequiredService<ISpectreConsoleLoggerControl>();
var console = services.GetRequiredService<IAnsiConsole>();

await control.FlushAsync(cancellationToken);
using (await control.TryAcquireRenderGateAsync(TimeSpan.FromSeconds(1), cancellationToken)
    ?? throw new TimeoutException("Console render gate unavailable."))
{
    lock (control.SynchronizationLock)
    {
        console.WriteLine("::endgroup::");
    }
}
```

Use `TryAcquireRenderGate` for synchronous callers. Both methods return an
`IDisposable` lease; the asynchronous method returns `null` on timeout. Take
`SynchronizationLock` only for the direct write itself, as shown, so new and
legacy integrations remain mutually exclusive.

CI hosts that prefer strict same-thread ordering over logging throughput can
skip the background channel entirely:

```csharp
builder.AddSpectreConsole(o => o.WriteMode = WriteMode.Synchronous);
```

In synchronous mode, an entry is rendered before `ILogger.Log` returns. Direct
writes should still use `SynchronizationLock` when other threads may write to
the same console.

## License

MIT
