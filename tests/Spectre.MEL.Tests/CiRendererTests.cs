using Microsoft.Extensions.Logging;
using Spectre.MEL.Ci;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class CiRendererTests
{
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
    public async Task AzurePipelines_emits_error_warning_debug_annotations()
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
        await Assert.That(output).Contains("##[debug]");
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
