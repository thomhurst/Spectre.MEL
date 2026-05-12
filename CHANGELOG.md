# Changelog

## Unreleased

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
- Options validated via `IValidateOptions<SpectreConsoleLoggerOptions>`.
- `PlaceholderStyleResolver` freezes after first resolve to guarantee
  thread-safety on the log path.
- TUnit + Spectre.Console.Testing-based test suite (72 tests).
- BenchmarkDotNet baseline against `Microsoft.Extensions.Logging.Console`.
