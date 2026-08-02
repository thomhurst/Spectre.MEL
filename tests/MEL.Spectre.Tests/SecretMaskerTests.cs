using MEL.Spectre.Masking;
using MEL.Spectre.Provider;
using MEL.Spectre.Rendering;
using MEL.Spectre.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class SecretMaskerTests
{
    private static SecretMasker BuildDefault()
    {
        var options = new SpectreConsoleLoggerOptions();
        return new SecretMasker(options.MaskedNamePatterns, options.MaskedValuePatterns, 256);
    }

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
    [Arguments("ghp_", 36)]
    [Arguments("ghs_", 36)]
    [Arguments("gho_", 36)]
    [Arguments("ghu_", 36)]
    [Arguments("ghr_", 36)]
    [Arguments("github_pat_", 22)]
    [Arguments("glpat-", 20)]
    [Arguments("xoxb-", 10)]
    public async Task Matches_default_prefixed_token_values(string prefix, int bodyLength)
    {
        var value = prefix + new string('a', bodyLength);
        await Assert.That(RenderDefaultValue(value)).IsEqualTo("***");
    }

    [Test]
    public async Task Matches_default_aws_access_key_value()
    {
        await Assert.That(RenderDefaultValue("AKIA1234567890ABCDEF")).IsEqualTo("***");
    }

    [Test]
    public async Task Matches_default_jwt_value()
    {
        await Assert.That(RenderDefaultValue("eyJaaaaaaaaaa.eyJbbbbbbbbbb.cccccccccc")).IsEqualTo("***");
    }

    [Test]
    [Arguments("-----BEGIN PRIVATE KEY-----")]
    [Arguments("-----BEGIN RSA PRIVATE KEY-----")]
    public async Task Matches_default_private_key_header_value(string value)
    {
        await Assert.That(RenderDefaultValue(value)).IsEqualTo("***");
    }

    [Test]
    [Arguments("ghp_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [Arguments("ghx_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [Arguments("GHP_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [Arguments("github_pat_aaaaaaaaaaaaaaaaaaaaa")]
    [Arguments("github_bad_aaaaaaaaaaaaaaaaaaaaaa")]
    [Arguments("glpat-aaaaaaaaaaaaaaaaaaa")]
    [Arguments("glpt-aaaaaaaaaaaaaaaaaaaa")]
    [Arguments("AKIA1234567890ABCDE")]
    [Arguments("ASIA1234567890ABCDEF")]
    [Arguments("xoxb-aaaaaaaaa")]
    [Arguments("xoxz-aaaaaaaaaa")]
    [Arguments("eyJaaaaaaaaa.eyJbbbbbbbbbb.cccccccccc")]
    [Arguments("-----BEGIN PUBLIC KEY-----")]
    public async Task Does_not_match_near_miss_token_values(string value)
    {
        await Assert.That(RenderDefaultValue(value)).IsEqualTo(value);
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
    public async Task TryMaskValuePatterns_masks_every_match_and_collects_original_values()
    {
        var masker = new SecretMasker([], [@"ghp_\w+"], 256);
        var collected = new List<string>();

        var found = masker.TryMaskValuePatterns(
            "first ghp_one then ghp_two",
            collected,
            out var masked);

        await Assert.That(found).IsTrue();
        await Assert.That(masked).IsEqualTo("first *** then ***");
        await Assert.That(collected).IsEquivalentTo(["ghp_one", "ghp_two"]);
    }

    [Test]
    public async Task TryMaskValuePatterns_does_not_match_synthetic_replacement_text()
    {
        var masker = new SecretMasker([], ["secret", @"\*\*\*"], 256);
        var collected = new List<string>();

        var found = masker.TryMaskValuePatterns("secret visible", collected, out var masked);

        await Assert.That(found).IsTrue();
        await Assert.That(masked).IsEqualTo("*** visible");
        await Assert.That(collected).IsEquivalentTo(["secret"]);
    }

    [Test]
    public async Task TryMaskValuePatterns_redacts_entire_value_for_zero_width_match()
    {
        var masker = new SecretMasker([], [@"(?=Bearer\s+\S+)"], 256);
        var collected = new List<string>();

        var found = masker.TryMaskValuePatterns("Bearer abc.def", collected, out var masked);

        await Assert.That(found).IsTrue();
        await Assert.That(masked).IsEqualTo("***");
        await Assert.That(collected).IsEquivalentTo(["Bearer abc.def"]);
    }

    [Test]
    public async Task Respects_cache_capacity()
    {
        var masker = new SecretMasker(["(?i)password"], valueCacheCapacity: 2);
        await Assert.That(masker.TryRegisterForEmission("a")).IsTrue();
        await Assert.That(masker.TryRegisterForEmission("b")).IsTrue();
        await Assert.That(masker.TryRegisterForEmission("c")).IsFalse();
    }

    [Test]
    public async Task Value_pattern_timeout_masks_the_whole_value()
    {
        var masker = new SecretMasker([], [@"(a+)+$|secret-\w+"], 256, TimeSpan.FromMilliseconds(1));
        var value = new string('a', 10_000) + "! secret-value";

        await Assert.That(masker.MaskValuePatterns(value)).IsEqualTo("***");
    }

    [Test]
    public async Task Value_pattern_timeout_fails_closed_through_try_mask()
    {
        var masker = new SecretMasker([], [@"(a+)+$|secret-\w+"], 256, TimeSpan.FromMilliseconds(1));
        var value = new string('a', 10_000) + "! secret-value";
        var collected = new List<string>();

        var matched = masker.TryMaskValuePatterns(value, collected, out var result);

        await Assert.That(matched).IsTrue();
        await Assert.That(result).IsEqualTo("***");
        await Assert.That(collected).IsEquivalentTo([value]);
    }

    [Test]
    public async Task Zero_length_value_pattern_masks_and_registers_the_whole_value()
    {
        const string Value = "prefix secret-value suffix";
        var masker = new SecretMasker([], [@"(?=secret-\w+)"], 256);
        var collected = new List<string>();

        var result = masker.MaskValuePatterns(Value, collected);

        await Assert.That(result).IsEqualTo("***");
        await Assert.That(collected).IsEquivalentTo([Value]);
    }

    private static string RenderDefaultValue(string value) =>
        MessageFormatter.Render(
            "{Output}",
            value,
            [new Placeholder("Output", value, typeof(string))],
            SpectreTheme.Monochrome,
            BuildDefault());
}
