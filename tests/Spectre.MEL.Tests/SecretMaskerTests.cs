using Spectre.MEL.Masking;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class SecretMaskerTests
{
    private static SecretMasker BuildDefault() => new SecretMasker(new SpectreConsoleLoggerOptions().MaskedNamePatterns, 256);

    [Test]
    [Arguments("password")]
    [Arguments("Password")]
    [Arguments("pwd")]
    [Arguments("token")]
    [Arguments("ApiKey")]
    [Arguments("authorization")]
    [Arguments("credential")]
    [Arguments("bearer_token")]
    public async Task Matches_default_patterns(string name)
    {
        var masker = BuildDefault();
        await Assert.That(masker.ShouldMask(name)).IsTrue();
    }

    [Test]
    [Arguments("username")]
    [Arguments("email")]
    [Arguments("userid")]
    public async Task Does_not_match_non_secret_names(string name)
    {
        var masker = BuildDefault();
        await Assert.That(masker.ShouldMask(name)).IsFalse();
    }

    [Test]
    public async Task Mask_returns_stars_for_strings()
    {
        await Assert.That(SecretMasker.Mask("abc")).IsEqualTo("***");
        await Assert.That(SecretMasker.Mask(null)).IsEqualTo("***");
    }

    [Test]
    public async Task TryRegisterForEmission_only_first_time()
    {
        var masker = BuildDefault();
        await Assert.That(masker.TryRegisterForEmission("v1")).IsTrue();
        await Assert.That(masker.TryRegisterForEmission("v1")).IsFalse();
        await Assert.That(masker.TryRegisterForEmission("v2")).IsTrue();
    }

    [Test]
    public async Task Respects_cache_capacity()
    {
        var masker = new SecretMasker(["(?i)password"], valueCacheCapacity: 2);
        await Assert.That(masker.TryRegisterForEmission("a")).IsTrue();
        await Assert.That(masker.TryRegisterForEmission("b")).IsTrue();
        await Assert.That(masker.TryRegisterForEmission("c")).IsFalse();
    }
}
