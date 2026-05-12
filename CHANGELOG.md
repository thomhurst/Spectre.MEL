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
- `SpectreTheme` and `PlaceholderStyleResolver` both freeze when the provider
  is constructed; mutations after that throw `InvalidOperationException`.
- Options validated via `IValidateOptions<SpectreConsoleLoggerOptions>` with
  `.ValidateOnStart()`: failures (bad template, invalid regex, out-of-range
  timeouts, `Wait` mode with zero timeout) surface at host startup.
- TUnit + Spectre.Console.Testing-based test suite (120 tests).
- BenchmarkDotNet baseline against `Microsoft.Extensions.Logging.Console`.
