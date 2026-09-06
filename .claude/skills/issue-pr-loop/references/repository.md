# Spectre.MEL

Read [CLAUDE.md](../../../../CLAUDE.md) for architecture, test commands, and configure-once invariants.

- Every local .NET command uses `scripts/Invoke-AgentDotNet.ps1`. Invoke it in-process in PowerShell (`& scripts/Invoke-AgentDotNet.ps1 -DotNetArguments @('build')`) so array arguments bind correctly. Allow the outer timeout at least 30 seconds beyond the guard. Exit 124/137 means a validation limit; report it and defer to CI rather than raising limits.
- Tests use TUnit executable projects, not VSTest. Guarded `dotnet build` also enforces lint/analyzers.
- Library awaits use `ConfigureAwait(false)`. Preserve immutable theme/masking snapshots. Avoid allocations, LINQ, closures, boxing, or async overhead per log message; measure write/render changes with relevant allocation benchmarks. Performance requirements also apply to review/simplification.
- No Aspire AppHost or external test services. Preserve shared lock Redis.
