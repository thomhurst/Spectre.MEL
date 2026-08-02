using BenchmarkDotNet.Attributes;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Rendering;
using MEL.Spectre.Theme;

namespace MEL.Spectre.Benchmarks;

[MemoryDiagnoser]
public class MessageMaskingBenchmarks
{
    private static readonly Placeholder[] NoPlaceholders = [];

    private readonly SecretMasker _masker = new([], [@"secret-\w+"], 256);
    private readonly SpectreTheme _theme = SpectreTheme.Monochrome;

    [Benchmark(Baseline = true)]
    public string ScanDisabled() =>
        MessageFormatter.Render("Ordinary log message", "fallback", NoPlaceholders, _theme, _masker, maskValuePatternsInMessageText: false);

    [Benchmark]
    public string ScanEnabledNoMatch() =>
        MessageFormatter.Render("Ordinary log message", "fallback", NoPlaceholders, _theme, _masker);

    [Benchmark]
    public string ScanEnabledMatch() =>
        MessageFormatter.Render("Token: secret-value", "fallback", NoPlaceholders, _theme, _masker);
}
