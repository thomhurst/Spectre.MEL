using Microsoft.Extensions.Options;
using Spectre.MEL;
using Spectre.MEL.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class OptionsValidatorTests
{
    private static SpectreConsoleLoggerOptionsValidator Validator => new();

    [Test]
    public async Task Defaults_validate_successfully()
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions());
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(-100)]
    public async Task ChannelCapacity_must_be_positive(int capacity)
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions { ChannelCapacity = capacity });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("ChannelCapacity");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Template_must_be_non_empty(string template)
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions { Template = template });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("Template");
    }

    [Test]
    public async Task Template_must_parse()
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions { Template = "{Level" });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("Template");
    }

    [Test]
    public async Task ShutdownDrainTimeout_must_be_non_negative()
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(-1) });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("ShutdownDrainTimeout");
    }

    [Test]
    public async Task EnqueueWaitTimeout_must_be_non_negative()
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions { EnqueueWaitTimeout = TimeSpan.FromMilliseconds(-1) });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("EnqueueWaitTimeout");
    }

    [Test]
    public async Task EnqueueWaitTimeout_cannot_exceed_ShutdownDrainTimeout()
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions
        {
            EnqueueWaitTimeout = TimeSpan.FromSeconds(10),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
        });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("EnqueueWaitTimeout");
    }

    [Test]
    public async Task MaskedValueCacheCapacity_must_be_non_negative()
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions { MaskedValueCacheCapacity = -1 });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("MaskedValueCacheCapacity");
    }

    [Test]
    public async Task Theme_must_not_be_null()
    {
        var result = Validator.Validate(null, new SpectreConsoleLoggerOptions { Theme = null! });
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("Theme");
    }

    [Test]
    public async Task MaskedNamePatterns_must_compile_as_regex()
    {
        var options = new SpectreConsoleLoggerOptions();
        options.MaskedNamePatterns.Add("(unbalanced");
        var result = Validator.Validate(null, options);
        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("MaskedNamePatterns");
    }
}
