using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Spectre.MEL;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class EntryFormatterTests
{
    [Test]
    [Arguments(LogLevel.Trace, "TRAC")]
    [Arguments(LogLevel.Debug, "DEBU")]
    [Arguments(LogLevel.Information, "INFO")]
    [Arguments(LogLevel.Warning, "WARN")]
    [Arguments(LogLevel.Error, "ERRO")]
    [Arguments(LogLevel.Critical, "CRIT")]
    public async Task Level_u4_truncates_to_four_uppercase(LogLevel level, string expected)
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.Log(level, "msg");
        }, o => o.Template = "[{Level:u4}] {Message}");

        await Assert.That(output).Contains($"[{expected}] msg");
    }

    [Test]
    public async Task Level_u6_pads_to_six_uppercase()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("msg");
        }, o => o.Template = "[{Level:u6}] {Message}");

        await Assert.That(output).Contains("[INFO  ] msg");
    }

    [Test]
    public async Task Level_l3_lowercases_and_truncates()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogWarning("msg");
        }, o => o.Template = "[{Level:l3}] {Message}");

        await Assert.That(output).Contains("[war] msg");
    }

    [Test]
    public async Task Level_L3_accepts_uppercase_L_prefix()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError("msg");
        }, o => o.Template = "[{Level:L3}] {Message}");

        await Assert.That(output).Contains("[err] msg");
    }

    [Test]
    public async Task Scope_token_joins_nested_labels_with_slash()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            using (logger.BeginScope("Outer"))
            using (logger.BeginScope("Inner"))
            {
                logger.LogInformation("x");
            }
        }, o => o.Template = "{Scope}|{Message}");

        await Assert.That(output).Contains("Outer / Inner|x");
    }

    [Test]
    public async Task TraceId_token_renders_active_activity_trace_id()
    {
        using var activity = new Activity("test").Start();
        var traceId = activity.TraceId.ToString();

        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("hello");
        }, o => o.Template = "{TraceId}|{Message}");

        await Assert.That(output).Contains($"{traceId}|hello");
    }

    [Test]
    public async Task SpanId_token_renders_active_activity_span_id()
    {
        using var activity = new Activity("test").Start();
        var spanId = activity.SpanId.ToString();

        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("hello");
        }, o => o.Template = "{SpanId}|{Message}");

        await Assert.That(output).Contains($"{spanId}|hello");
    }

    [Test]
    public async Task Custom_placeholder_token_renders_value_from_state()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("Order {OrderId} done", 7);
        }, o => o.Template = "id={OrderId}|{Message}");

        await Assert.That(output).Contains("id=7|Order 7 done");
    }

    [Test]
    public async Task EventId_token_renders_id_and_optional_name()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation(new EventId(42, "OrderPlaced"), "msg");
        }, o => o.Template = "{EventId} {Message}");

        await Assert.That(output).Contains("#42 OrderPlaced");
    }
}
