# Changelog

## Unreleased

## 0.6.0 - 2026-07-22

### Added

- Embedded ANSI handling through `EmbeddedAnsiMode`: convert SGR styles to
  balanced Spectre markup by default, strip them, or pass them through.
- Sanitization for non-SGR terminal control sequences in relayed child-process
  output and CI annotation payloads.

### Changed

- Updated the .NET SDK, Microsoft.Extensions packages, Spectre.Console, test
  dependencies, and GitVersion action.

## 0.5.0 - 2026-07-21

### Added

- `IAnsiConsole` registration for consumers that need the provider's configured
  console, plus `WriteJsonPanel` and `LogScopeOutcome` helpers.
- `MinimumInlineLevel` and configurable per-level native CI annotations.
- `ISpectreConsoleLoggerControl.FlushAsync`, a shared synchronization lock, and
  `WriteMode.Synchronous` for ordering-sensitive output.
- Value-pattern masking with defaults for common GitHub, GitLab, AWS, Slack,
  JWT, and private-key credentials.

### Changed

- CI workflow commands and masks are emitted as raw, correctly encoded lines.
- Native annotation payloads use message-only plain text and CI output no
  longer wraps by default.
- Log levels use conventional names and debug annotations are opt-in.

## 0.4.0 - 2026-05-12

- Initial release.
- `AddSpectreConsole()` on `ILoggingBuilder`.
- Built-in themes: Default, Dark, Light, Monochrome.
- Native CI renderers: GitHub Actions, Azure Pipelines, GitLab CI, TeamCity,
  Buildkite, Travis.
- Passthrough CI detection for Jenkins, CircleCI, AppVeyor (plain ANSI, no
  grouping or annotations).
- Secret masking with `::add-mask::` integration on GitHub Actions.
- Channel-based background writer with bounded backpressure (`Wait`,
  `DropNewest`, `DropOldest`) and configurable `EnqueueWaitTimeout`.
- `SpectreTheme` and `PlaceholderStyleResolver` both freeze when the provider
  is constructed; mutations after that throw `InvalidOperationException`.
- Options validated via `IValidateOptions<SpectreConsoleLoggerOptions>` with
  `.ValidateOnStart()`: failures (bad template, invalid regex, out-of-range
  timeouts, `Wait` mode with zero timeout) surface at host startup.
- TUnit + Spectre.Console.Testing-based test suite (120 tests).
- BenchmarkDotNet baseline against `Microsoft.Extensions.Logging.Console`.
