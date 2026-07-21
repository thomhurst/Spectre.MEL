using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Testing;
using MEL.Spectre;
using MEL.Spectre.Scopes;
using MEL.Spectre.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class OutputImprovementsTests
{
    [Test]
    public async Task IAnsiConsole_is_resolvable_from_DI_and_is_the_one_provider_uses()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 1_000_000;

        await using var sp = new ServiceCollection()
            .AddLogging(b => b.AddSpectreConsole(o =>
            {
                o.Console = captured;
                o.CiMode = CiMode.Off;
                o.Theme = SpectreTheme.Monochrome;
            }))
            .BuildServiceProvider();

        var resolved = sp.GetRequiredService<IAnsiConsole>();

        await Assert.That(resolved).IsSameReferenceAs(captured);
    }

    [Test]
    public async Task IAnsiConsole_DI_factory_builds_wide_profile_when_no_console_provided()
    {
        await using var sp = new ServiceCollection()
            .AddLogging(b => b.AddSpectreConsole(o =>
            {
                o.CiMode = CiMode.Off;
                o.InteractivityMode = InteractivityMode.NonInteractive;
                o.Theme = SpectreTheme.Monochrome;
            }))
            .BuildServiceProvider();

        var resolved = sp.GetRequiredService<IAnsiConsole>();

        await Assert.That(resolved.Profile.Width).IsEqualTo(1_000_000);
    }

    [Test]
    public async Task MinimumInlineLevel_suppresses_info_level_and_strips_surrounding_brackets()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("hello");
            logger.LogWarning("careful");
        }, o =>
        {
            o.MinimumInlineLevel = LogLevel.Warning;
            o.Template = "[{Level:u5}] {Message}";
        });

        await Assert.That(output).Contains("hello");
        await Assert.That(output).DoesNotContain("[INFO ] hello");
        await Assert.That(output).Contains("[WARN ] careful");
    }

    [Test]
    public async Task MinimumInlineLevel_with_bracketed_template_yields_no_empty_brackets()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("hello");
        }, o =>
        {
            o.MinimumInlineLevel = LogLevel.Warning;
            o.Template = "[{Level:u5}] {Message}";
        });

        await Assert.That(output).DoesNotContain("[]");
        await Assert.That(output).DoesNotContain("[ ]");
        await Assert.That(output).StartsWith("hello");
    }

    [Test]
    public async Task SuppressInlineLevelOnCiAnnotation_strips_surrounding_brackets()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogWarning("careful");
            logger.LogInformation("just info");
        }, o =>
        {
            o.SuppressInlineLevelOnCiAnnotation = true;
            o.Template = "[{Level:u5}] {Message}";
        });

        await Assert.That(output).Contains("::warning::");
        // Warning line: GHA annotation only, brackets and level stripped.
        await Assert.That(output).Contains("::warning::careful");
        await Assert.That(output).DoesNotContain("[] careful");
        // Info line: no GHA annotation, keep the existing bracketed level.
        await Assert.That(output).Contains("[INFO ] just info");
    }

    [Test]
    public async Task LogScopeOutcome_writes_structured_success_line_with_duration()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogScopeOutcome(ScopeOutcome.Success, "Build", TimeSpan.FromMilliseconds(1234));
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("✓");
        await Assert.That(output).Contains("Build");
        await Assert.That(output).Contains("1.2s");
    }

    [Test]
    public async Task LogScopeOutcome_failure_logs_at_error_level()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogScopeOutcome(ScopeOutcome.Failure, "Tests");
        }, o => o.Template = "{Level:u} {Message}");

        await Assert.That(output).Contains("::error::");
        await Assert.That(output).Contains("✗");
        await Assert.That(output).Contains("Tests");
    }

    [Test]
    public async Task WriteJsonPanel_emits_group_markers_when_in_github_actions()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 200;

        captured.WriteJsonPanel("Git Info", new { Branch = "main", Sha = "abc123" }, CiMode.GitHubActions);

        var output = captured.Output;
        await Assert.That(output).Contains("::group::Git Info");
        await Assert.That(output).Contains("::endgroup::");
        await Assert.That(output).Contains("\"Branch\"");
        await Assert.That(output).Contains("main");
    }

    [Test]
    public async Task WriteJsonPanel_skips_group_markers_when_ci_off()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 200;

        captured.WriteJsonPanel("Config", new { Verbose = true }, CiMode.Off);

        var output = captured.Output;
        await Assert.That(output).DoesNotContain("::group::");
        await Assert.That(output).DoesNotContain("::endgroup::");
        await Assert.That(output).DoesNotContain("##[group]");
        await Assert.That(output).Contains("Config");
        await Assert.That(output).Contains("Verbose");
    }

    [Test]
    public async Task WriteJsonPanel_handles_null_payload()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 200;

        captured.WriteJsonPanel("Empty", null, CiMode.Off);

        await Assert.That(captured.Output).Contains("null");
    }

    [Test]
    public async Task MaskedValuePatterns_masks_secret_in_innocuously_named_placeholder()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("Auth header: {Header}", "Bearer abc.def.ghi");
        }, o =>
        {
            o.MaskedValuePatterns.Add(@"^Bearer\s+\S+");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("Auth header: ***");
        await Assert.That(output).DoesNotContain("abc.def.ghi");
    }

    [Test]
    public async Task MaskedValuePatterns_emits_add_mask_in_github_actions()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogInformation("Token: {Tok}", "ghp_abcd1234");
        }, o =>
        {
            o.MaskedValuePatterns.Add(@"^ghp_\w+");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("::add-mask::ghp_abcd1234");
        await Assert.That(output).Contains("Token: ***");
    }

    [Test]
    public async Task MaskedValuePatterns_does_nothing_for_non_matching_value()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("Value: {V}", "ordinary string");
        }, o =>
        {
            o.MaskedValuePatterns.Add(@"^Bearer\s+\S+");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("ordinary string");
        await Assert.That(output).DoesNotContain("***");
    }
}
