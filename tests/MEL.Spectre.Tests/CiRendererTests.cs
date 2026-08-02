using Microsoft.Extensions.Logging;
using MEL.Spectre.Ci;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace MEL.Spectre.Tests;

public class CiRendererTests
{
    private const string AddMaskPrefix = "::add-mask::";

    [Test]
    public async Task AzurePipelines_emits_group_endgroup_markers()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.AzurePipelines, logger =>
        {
            using (logger.BeginScope("Outer"))
            {
                logger.LogInformation("inside");
            }
        });

        await Assert.That(output).Contains("##[group]Outer");
        await Assert.That(output).Contains("##[endgroup]");
    }

    [Test]
    public async Task AzurePipelines_emits_error_and_warning_annotations_by_default()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.AzurePipelines, logger =>
        {
            logger.LogError("err");
            logger.LogWarning("warn");
            logger.LogDebug("dbg");
            logger.LogInformation("info");
        });

        await Assert.That(output).Contains("##[error]");
        await Assert.That(output).Contains("##[warning]");
        await Assert.That(output).DoesNotContain("##[debug]");
        await Assert.That(output).Contains("dbg");
        await Assert.That(output).DoesNotContain("##[info]");
    }

    [Test]
    public async Task GitLabCi_emits_section_start_end_with_scope_id()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitLabCi, logger =>
        {
            using (logger.BeginScope("Build phase"))
            {
                logger.LogInformation("step");
            }
        });

        await Assert.That(output).Contains("section_start:");
        await Assert.That(output).Contains("section_end:");
        await Assert.That(output).Contains("Build phase");
    }

    [Test]
    public async Task TeamCity_emits_blockOpened_and_blockClosed()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.TeamCity, logger =>
        {
            using (logger.BeginScope("Compile"))
            {
                logger.LogInformation("hello");
            }
        });

        await Assert.That(output).Contains("##teamcity[blockOpened name='Compile']");
        await Assert.That(output).Contains("##teamcity[blockClosed name='Compile']");
    }

    [Test]
    public async Task TeamCity_emits_message_for_warning_and_error_only()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.TeamCity, logger =>
        {
            logger.LogInformation("info");
            logger.LogWarning("warn");
            logger.LogError("err");
        });

        await Assert.That(output).Contains("##teamcity[message");
        await Assert.That(output).Contains("status='WARNING'");
        await Assert.That(output).Contains("status='ERROR'");

        var infoMessageIndex = output.IndexOf("##teamcity[message text='info'", StringComparison.Ordinal);
        await Assert.That(infoMessageIndex).IsEqualTo(-1);
    }

    [Test]
    public async Task TeamCity_escapes_pipe_quote_brackets_newlines_in_scope_label()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.TeamCity, logger =>
        {
            using (logger.BeginScope("a'b|c[d]\ne"))
            {
                logger.LogInformation("x");
            }
        });

        await Assert.That(output).Contains("##teamcity[blockOpened name='a|'b||c|[d|]|ne']");
    }

    [Test]
    public async Task Buildkite_emits_dash_section_open_only()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Buildkite, logger =>
        {
            using (logger.BeginScope("Phase A"))
            {
                logger.LogInformation("x");
            }
        });

        await Assert.That(output).Contains("--- Phase A");
        await Assert.That(output).DoesNotContain("--- end");
    }

    [Test]
    public async Task Travis_emits_fold_start_and_end()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Travis, logger =>
        {
            using (logger.BeginScope("Tests"))
            {
                logger.LogInformation("x");
            }
        });

        await Assert.That(output).Contains("travis_fold:start:scope_");
        await Assert.That(output).Contains("travis_fold:end:scope_");
        await Assert.That(output).Contains("Tests");
    }

    [Test]
    [Arguments(CiMode.Jenkins)]
    [Arguments(CiMode.CircleCi)]
    [Arguments(CiMode.AppVeyor)]
    public async Task Passthrough_renderers_emit_no_native_markers(CiMode mode)
    {
        var output = await LogTestHarness.CaptureAsync(mode, logger =>
        {
            using (logger.BeginScope("Outer"))
            {
                logger.LogError("fail");
            }
        });

        await Assert.That(output).Contains("fail");
        await Assert.That(output).DoesNotContain("::group::");
        await Assert.That(output).DoesNotContain("##[group]");
        await Assert.That(output).DoesNotContain("section_start:");
        await Assert.That(output).DoesNotContain("##teamcity[");
        await Assert.That(output).DoesNotContain("travis_fold:");
        await Assert.That(output).DoesNotContain("--- ");
    }

    [Test]
    public async Task GitHubActions_debug_is_visible_without_annotation_by_default()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogDebug("visible debug");
        });

        await Assert.That(output).Contains("DEBU");
        await Assert.That(output).Contains("visible debug");
        await Assert.That(output).DoesNotContain("::debug::");
    }

    [Test]
    public async Task GitHubActions_debug_annotation_can_be_enabled_explicitly()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogDebug("opted-in debug");
        }, o => o.CiLevelAnnotations[LogLevel.Debug] = CiAnnotation.Debug);

        await Assert.That(output).Contains("::debug::");
        await Assert.That(output).Contains("opted-in debug");
    }

    [Test]
    public async Task SuppressInlineLevelOnCiAnnotation_drops_level_tag_for_warning_and_error()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogWarning("watch out");
            logger.LogError("kaboom");
            logger.LogInformation("just info");
        }, o =>
        {
            o.SuppressInlineLevelOnCiAnnotation = true;
            o.Template = "[{Level:u5}] {Message}";
        });

        await Assert.That(output).Contains("::warning::");
        await Assert.That(output).Contains("::error::");
        await Assert.That(output).DoesNotContain("[WARN ]");
        await Assert.That(output).DoesNotContain("[ERROR]");
        await Assert.That(output).Contains("[INFO ] just info");
    }

    [Test]
    public async Task SuppressInlineLevelOnCiAnnotation_keeps_level_tag_for_info_when_no_ci_annotation()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogInformation("hi");
        }, o =>
        {
            o.SuppressInlineLevelOnCiAnnotation = true;
            o.Template = "[{Level:u5}] {Message}";
        });

        await Assert.That(output).Contains("[INFO ] hi");
    }

    [Test]
    public async Task Ci_annotations_suppress_inline_level_by_default()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogWarning("watch out");
        }, o => o.Template = "[{Level:u5}] {Message}");

        await Assert.That(output).Contains("::warning::");
        await Assert.That(output).DoesNotContain("[WARN ]");
        await Assert.That(output).Contains("::warning::watch out");
    }

    [Test]
    public async Task Ci_annotation_level_suppression_can_be_disabled()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogWarning("watch out");
        }, o =>
        {
            o.SuppressInlineLevelOnCiAnnotation = false;
            o.Template = "[{Level:u5}] {Message}";
        });

        await Assert.That(output).Contains("::warning::[WARN ] watch out");
    }

    [Test]
    public async Task Ci_annotation_default_uses_message_only_for_custom_template()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogWarning("watch out");
        }, o => o.Template = "{Timestamp:HH:mm:ss} {Category} {Level:u5} - {Message}");

        await Assert.That(output).Contains("::warning::watch out");
        await Assert.That(output).DoesNotContain(" - watch out");
        await Assert.That(output).DoesNotContain("MEL.Spectre.Tests");
    }

    [Test]
    public async Task GitHubActions_annotation_payload_is_plain_text()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogWarning("[red]⚠[/] unregistered modules");
        }, o =>
        {
            o.AllowMarkupInMessageTemplate = true;
            o.Theme = MEL.Spectre.Theme.SpectreTheme.Default;
            o.Template = "[{Level:u5}] {Message}";
        });

        await Assert.That(output).Contains("::warning::⚠ unregistered modules");
        await Assert.That(output).DoesNotContain("\u001b");
        await Assert.That(output).DoesNotContain("[red]");
        await Assert.That(output).DoesNotContain("[WARN ]");
    }

    [Test]
    public async Task GitHubActions_multiline_annotation_is_one_escaped_command()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogWarning("first 100%\r\nsecond\nthird");
        }, o => o.Template = "{Message}");

        var annotationLines = GetPhysicalLines(output)
            .Where(line => line.StartsWith("::warning::", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(annotationLines).Count().IsEqualTo(1);
        await Assert.That(annotationLines[0]).IsEqualTo("::warning::first 100%25%0D%0Asecond%0Athird");
    }

    [Test]
    public async Task AllowMarkupInMessageTemplate_passes_through_markup_tags()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("[green]OK[/] for {Name}", "auth");
        }, o =>
        {
            o.AllowMarkupInMessageTemplate = true;
            o.Theme = MEL.Spectre.Theme.SpectreTheme.Default;
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("OK");
        await Assert.That(output).DoesNotContain("[green]");
        await Assert.That(output).DoesNotContain("[/]");
    }

    [Test]
    public async Task LogMarkup_enables_markup_for_only_that_entry()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogMarkup("[green]trusted[/]");
            logger.LogInformation("[green]literal[/]");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("trusted");
        await Assert.That(output).DoesNotContain("[green]trusted[/]");
        await Assert.That(output).Contains("[green]literal[/]");
    }

    [Test]
    public async Task LogMarkup_CI_annotation_payload_is_plain_text()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogMarkup(LogLevel.Warning, "[red]danger[/]");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("::warning::danger");
        await Assert.That(output).DoesNotContain("[red]");
    }

    [Test]
    public async Task AllowMarkupInMessageTemplate_off_escapes_markup_tags()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogInformation("[green]OK[/] for {Name}", "auth");
        }, o =>
        {
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("[green]OK[/]");
    }

    [Test]
    public async Task GitHubActions_emits_add_mask_only_once_per_value()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogInformation("Auth {Authorization}", "Bearer xyz");
            logger.LogInformation("Auth again {Authorization}", "Bearer xyz");
            logger.LogInformation("Different {Authorization}", "Bearer different");
        });

        var maskCount = CountSubstring(output, "::add-mask::Bearer xyz");
        var differentMaskCount = CountSubstring(output, "::add-mask::Bearer different");

        await Assert.That(maskCount).IsEqualTo(1);
        await Assert.That(differentMaskCount).IsEqualTo(1);
    }

    [Test]
    public async Task GitHubActions_long_mask_command_is_one_physical_line()
    {
        var secret = "Bearer " + new string('s', 200);
        var output = await CaptureAtWidthAsync(
            CiMode.GitHubActions,
            20,
            logger => logger.LogInformation("Auth {Authorization}", secret));

        var maskLines = GetPhysicalLines(output)
            .Where(line => line.StartsWith("::add-mask::", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(maskLines).Count().IsEqualTo(1);
        await Assert.That(maskLines[0]).IsEqualTo("::add-mask::" + secret);
    }

    [Test]
    public async Task GitHubActions_mask_emission_bypasses_consumer_console_width()
    {
        var secret = new string('s', 200);
        var output = await CaptureMaskedSecretAsync(secret, consoleWidth: 80);
        var maskLines = GetMaskLines(output);

        await Assert.That(maskLines).Count().IsEqualTo(1);
        await Assert.That(maskLines[0]).IsEqualTo(AddMaskPrefix + secret);
    }

    [Test]
    public async Task GitHubActions_mask_escapes_workflow_command_percent_sequences()
    {
        var output = await CaptureMaskedSecretAsync("literal%25secret", consoleWidth: 80);
        var maskLines = GetMaskLines(output);

        await Assert.That(maskLines).Count().IsEqualTo(1);
        await Assert.That(maskLines[0]).IsEqualTo(AddMaskPrefix + "literal%2525secret");
    }

    [Test]
    public async Task GitHubActions_long_mask_is_registered_in_full_and_as_raw_chunks()
    {
        var secret = new string('s', 5_000);
        var output = await CaptureMaskedSecretAsync(secret, consoleWidth: 80);
        var lines = GetPhysicalLines(output);
        var maskLines = GetMaskLines(output);

        await Assert.That(maskLines).Count().IsGreaterThan(2);
        await Assert.That(maskLines[0]).IsEqualTo(AddMaskPrefix + secret);

        var registeredValue = string.Concat(maskLines.Skip(1).Select(line => line[AddMaskPrefix.Length..]));
        await Assert.That(registeredValue).IsEqualTo(secret);

        foreach (var line in lines.Where(line => !line.StartsWith(AddMaskPrefix, StringComparison.Ordinal)))
        {
            await Assert.That(line).DoesNotContain(new string('s', 80));
        }
    }

    [Test]
    public async Task GitHubActions_long_mask_overlaps_short_final_chunk()
    {
        var secret = new string('s', 1_000) + "a";
        var output = await CaptureMaskedSecretAsync(secret, consoleWidth: 80);
        var maskLines = GetMaskLines(output);

        await Assert.That(maskLines).Count().IsEqualTo(3);
        await Assert.That(maskLines[0]).IsEqualTo(AddMaskPrefix + secret);
        await Assert.That(maskLines[1]).IsEqualTo(AddMaskPrefix + secret[..1_000]);
        await Assert.That(maskLines[2]).IsEqualTo(AddMaskPrefix + secret[^1_000..]);
        await Assert.That(maskLines).DoesNotContain(AddMaskPrefix + "a");
    }

    [Test]
    public async Task GitHubActions_multiline_mask_registers_full_value_and_each_line_raw()
    {
        var output = await CaptureMaskedSecretAsync("first\nsecond\r\nthird", consoleWidth: 5);
        var maskLines = GetMaskLines(output);

        await Assert.That(maskLines).Count().IsEqualTo(4);
        await Assert.That(maskLines[0]).IsEqualTo(AddMaskPrefix + "first%0Asecond%0D%0Athird");
        await Assert.That(maskLines[1]).IsEqualTo(AddMaskPrefix + "first");
        await Assert.That(maskLines[2]).IsEqualTo(AddMaskPrefix + "second");
        await Assert.That(maskLines[3]).IsEqualTo(AddMaskPrefix + "third");
    }

    [Test]
    public async Task GitHubActions_multiline_mask_does_not_register_tiny_physical_lines()
    {
        var output = await CaptureMaskedSecretAsync("{\nsecret-value\n  a  \n}", consoleWidth: 5);
        var maskLines = GetMaskLines(output);

        await Assert.That(maskLines).Count().IsEqualTo(1);
        await Assert.That(maskLines[0]).IsEqualTo(AddMaskPrefix + "secret-value");
    }

    [Test]
    public async Task GitHubActions_long_group_command_is_one_physical_line()
    {
        var label = new string('g', 200);
        var output = await CaptureAtWidthAsync(CiMode.GitHubActions, 20, logger =>
        {
            using (logger.BeginScope(label))
            {
                logger.LogInformation("inside");
            }
        });

        var groupLines = GetPhysicalLines(output)
            .Where(line => line.StartsWith("::group::", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(groupLines).Count().IsEqualTo(1);
        await Assert.That(groupLines[0]).IsEqualTo("::group::" + label);
    }

    [Test]
    [Arguments(CiMode.AzurePipelines)]
    [Arguments(CiMode.GitLabCi)]
    [Arguments(CiMode.TeamCity)]
    [Arguments(CiMode.Buildkite)]
    [Arguments(CiMode.Travis)]
    public async Task Native_ci_group_commands_do_not_wrap(CiMode mode)
    {
        var label = new string('g', 200);
        var output = await CaptureAtWidthAsync(mode, 20, logger =>
        {
            using (logger.BeginScope(label))
            {
                logger.LogInformation("inside");
            }
        });

        var labelLines = GetPhysicalLines(output)
            .Where(line => line.Contains("ggggg", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(labelLines).Count().IsGreaterThanOrEqualTo(1);
        foreach (var line in labelLines)
        {
            await Assert.That(line).Contains(label);
        }
    }

    [Test]
    public async Task GitHubActions_long_warning_command_is_one_physical_line()
    {
        var message = new string('w', 200);
        var output = await CaptureAtWidthAsync(
            CiMode.GitHubActions,
            20,
            logger => logger.LogWarning("{Message}", message),
            o => o.Template = "{Message}");

        var annotationLines = GetPhysicalLines(output)
            .Where(line => line.StartsWith("::warning::", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(annotationLines).Count().IsEqualTo(1);
        await Assert.That(annotationLines[0]).IsEqualTo("::warning::" + message);
    }

    private static async Task<string> CaptureAtWidthAsync(
        CiMode mode,
        int width,
        Action<ILogger> log,
        Action<SpectreConsoleLoggerOptions>? configure = null)
    {
        var (console, services, logger) = LogTestHarness.Build(mode, configure);
        console.Profile.Width = width;

        try
        {
            log(logger);
        }
        finally
        {
            await services.DisposeAsync();
        }

        return console.Output;
    }

    private static async Task<string> CaptureMaskedSecretAsync(string secret, int consoleWidth)
    {
        var (console, services, logger) = LogTestHarness.Build(
            CiMode.GitHubActions,
            o => o.Template = "{Message}");
        console.Profile.Width = consoleWidth;

        try
        {
            logger.LogInformation("Secret {Password}", secret);
        }
        finally
        {
            await services.DisposeAsync();
        }

        return console.Output;
    }

    private static string[] GetMaskLines(string output) =>
        GetPhysicalLines(output)
            .Where(line => line.StartsWith(AddMaskPrefix, StringComparison.Ordinal))
            .ToArray();

    private static string[] GetPhysicalLines(string output) =>
        output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static int CountSubstring(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }
}
