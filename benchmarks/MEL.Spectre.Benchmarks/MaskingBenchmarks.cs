using BenchmarkDotNet.Attributes;
using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Rendering;
using MEL.Spectre.Theme;

namespace MEL.Spectre.Benchmarks;

[MemoryDiagnoser]
public class MaskingBenchmarks
{
    private readonly SpectreTheme _theme = SpectreTheme.Monochrome;
    private SecretMasker _masker = null!;

    [Params(false, true)]
    public bool DefaultValuePatterns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var options = new SpectreConsoleLoggerOptions();
        IEnumerable<string> valuePatterns = DefaultValuePatterns
            ? options.MaskedValuePatterns
            : Array.Empty<string>();
        _masker = new SecretMasker(options.MaskedNamePatterns, valuePatterns, 256);
    }

    [Benchmark]
    public string Render_without_placeholders() =>
        MessageFormatter.Render(
            "Application started",
            "Application started",
            Array.Empty<Placeholder>(),
            _theme,
            _masker);
}
