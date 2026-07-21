# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

MEL.Spectre — a `Microsoft.Extensions.Logging` provider (NuGet package) that renders log entries through Spectre.Console with CI-runner awareness (GitHub Actions, Azure Pipelines, GitLab CI, TeamCity, Buildkite, Travis natively; Jenkins/CircleCI/AppVeyor via passthrough). Single library project targeting `net10.0`; SDK version pinned in `global.json`. Solution file is `MEL.Spectre.slnx`.

Note: `agents.md` is a symlink to this file.

## Commands

```sh
dotnet build                          # also the lint: TreatWarningsAsErrors + EnforceCodeStyleInBuild + latest-recommended analyzers

# Tests use TUnit on Microsoft.Testing.Platform — run via `dotnet run`, not `dotnet test`
dotnet run --project tests/MEL.Spectre.Tests/MEL.Spectre.Tests.csproj -- --treenode-filter "/**"

# Single test class / single test (filter format: /Assembly/Namespace/Class/Test)
dotnet run --project tests/MEL.Spectre.Tests/MEL.Spectre.Tests.csproj -- --treenode-filter "/*/*/EntryFormatterTests/*"
dotnet run --project tests/MEL.Spectre.Tests/MEL.Spectre.Tests.csproj -- --treenode-filter "/*/*/EntryFormatterTests/Renders_Timestamp"

dotnet pack src/MEL.Spectre/MEL.Spectre.csproj --configuration Release
dotnet run --project benchmarks/MEL.Spectre.Benchmarks -c Release
```

Package versions are managed centrally in `Directory.Packages.props` (Central Package Management) — csproj files reference packages without versions. Shared build settings live in `Directory.Build.props`.

Releases: manually dispatched `publish.yml` workflow; versioning via GitVersion (`GitVersion.yml`).

## Architecture

Everything in `src/MEL.Spectre` is `internal` except the public options/extension/theme surface; tests and benchmarks see internals via `InternalsVisibleTo`.

**Wiring (`SpectreConsoleLoggingBuilderExtensions.AddSpectreConsole`)**: registers `SpectreConsoleLoggerOptions` with `ValidateOnStart()` + `SpectreConsoleLoggerOptionsValidator` (invalid regexes/templates/timeouts fail at host startup), removes any registered `ConsoleLoggerProvider` to avoid duplicate output, and registers `IAnsiConsole` (via `AnsiConsoleFactory`) and the provider as singletons.

**Provider construction (`Provider/SpectreConsoleLoggerProvider`)** builds the whole pipeline once, immutably:
- `CiDetector` resolves `CiMode.Auto` from env vars (`Ci/KnownEnvVars.cs`), then `ResolveRenderer` picks the `ICiRenderer` — one per runner in `Ci/Renderers/`, `PassthroughCiRenderer` for runners without native group/annotation syntax, `PlainTtyRenderer` when not in CI. Renderer capabilities (grouping, ANSI, level annotations, masking) are declared via `CiCapabilities`.
- `SpectreTheme.Freeze()` is called here — the theme and its `PlaceholderStyleResolver` are configure-once and throw `InvalidOperationException` on mutation after freeze. `MaskedNamePatterns` is likewise snapshotted into `SecretMasker` at construction. Preserve this invariant when adding options.
- `OutputTemplate`/`TemplateParser` + theme + `SecretMasker` compose into `EntryFormatter` → `RendererContext` → renderer.

**Log path**: `SpectreConsoleLogger.Log` runs on the caller's thread — it extracts placeholders from state via `StateReader`, captures scope frames and `Activity` ids, and enqueues an immutable `LogEntry` into `BackgroundWriter`. The writer owns a bounded `Channel<LogEntry>` with a single consumer task that renders in order. Backpressure per `BackpressureMode` (Wait spins then waits up to `EnqueueWaitTimeout`, or drop newest/oldest); all drop paths increment counters and emit a one-shot stderr warning via `OnceFlag`. Exception filters use `FatalExceptions.IsFatal` — rendering faults are swallowed and reported to stderr, never thrown into user code.

**Scope rendering**: scopes are captured as `ScopeFrame[]` per entry; the consumer diffs the incoming frames against its active-scope stack (`ReconcileScopes`) and emits open/close calls so CI renderers can produce collapsible groups. Dispose drains the channel up to `ShutdownDrainTimeout` and closes any open scopes.

## Tests

TUnit + TUnit.Assertions + Verify.TUnit, with `Spectre.Console.Testing`'s `TestConsole` for output capture. The standard pattern is `LogTestHarness.CaptureAsync(ciMode, logger => ...)`: it builds a real DI logging stack against a `TestConsole` (Monochrome theme, very wide profile to avoid wrapping), disposes the service provider to drain the background writer, and returns the console output for assertion. CI detection tests use the `CiDetector.DetectFromEnvironment(IDictionary)` overload rather than mutating real env vars.
