using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Testing;
using Spectre.MEL;
using Spectre.MEL.Theme;

namespace Spectre.MEL.Benchmarks;

[MemoryDiagnoser]
public class LoggingBenchmarks
{
    private ServiceProvider _spectreServices = null!;
    private ServiceProvider _consoleServices = null!;
    private ILogger _spectreLogger = null!;
    private ILogger _consoleLogger = null!;
    private TestConsole _captured = null!;

    [GlobalSetup]
    public void Setup()
    {
        _captured = new TestConsole { Profile = { Width = 1_000_000 } };

        _spectreServices = new ServiceCollection()
            .AddLogging(b => b.AddSpectreConsole(o =>
            {
                o.Console = _captured;
                o.Theme = SpectreTheme.Monochrome;
                o.CiMode = CiMode.Off;
                o.IncludeScopes = false;
            }))
            .BuildServiceProvider();
        _spectreLogger = _spectreServices.GetRequiredService<ILoggerFactory>().CreateLogger("Bench");

        _consoleServices = new ServiceCollection()
            .AddLogging(b => b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.None))
            .BuildServiceProvider();
        _consoleLogger = _consoleServices.GetRequiredService<ILoggerFactory>().CreateLogger("Bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _spectreServices.Dispose();
        _consoleServices.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void MicrosoftConsole_Information()
    {
        _consoleLogger.LogInformation("User {UserId} logged in from {Host}", 42, "node-1");
    }

    [Benchmark]
    public void SpectreMel_Information()
    {
        _spectreLogger.LogInformation("User {UserId} logged in from {Host}", 42, "node-1");
    }

    [Benchmark]
    public void SpectreMel_Warning_With_Exception()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            _spectreLogger.LogWarning(ex, "Operation {Operation} failed", "Process");
        }
    }
}
