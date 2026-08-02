using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    public async Task Ci_logs_do_not_wrap_on_supplied_narrow_console()
    {
        var message = new string('x', 500);
        var (output, consoleWidth) = await CaptureAtWidthAsync(CiMode.GitHubActions, 80, message);

        var lines = GetPhysicalLines(output);
        await Assert.That(lines).Count().IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo(message);
        await Assert.That(consoleWidth).IsEqualTo(80);
    }

    [Test]
    public async Task Ci_logs_do_not_wrap_beyond_one_million_characters()
    {
        var message = new string('x', 1_000_001);
        var (output, _) = await CaptureAtWidthAsync(CiMode.GitHubActions, 80, message);

        var lines = GetPhysicalLines(output);
        await Assert.That(lines).Count().IsEqualTo(1);
        await Assert.That(lines[0].Length).IsEqualTo(message.Length);
    }

    [Test]
    public async Task Non_ci_logs_still_follow_supplied_console_width()
    {
        var (output, _) = await CaptureAtWidthAsync(CiMode.Off, 80, new string('x', 500));

        await Assert.That(GetPhysicalLines(output)).Count().IsGreaterThan(1);
    }

    [Test]
    public async Task WrapInCi_uses_supplied_console_width()
    {
        var (output, _) = await CaptureAtWidthAsync(
            CiMode.GitHubActions,
            80,
            new string('x', 500),
            o => o.WrapInCi = true);

        await Assert.That(GetPhysicalLines(output)).Count().IsGreaterThan(1);
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
    public async Task WriteJsonPanel_uses_provided_type_info()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 200;
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var typeInfo = (JsonTypeInfo<Dictionary<string, bool>>)options.GetTypeInfo(typeof(Dictionary<string, bool>));

        captured.WriteJsonPanelTrimSafe("Config", new Dictionary<string, bool> { ["Verbose"] = true }, typeInfo, CiMode.Off);

        await Assert.That(captured.Output).Contains("Verbose");
        await Assert.That(captured.Output).Contains("true");
    }

    [Test]
    public async Task WriteJsonPanel_uses_provided_type_info_for_null_payload()
    {
        var captured = new TestConsole();
        captured.Profile.Width = 200;
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new NullStringConverter());
        var typeInfo = (JsonTypeInfo<string>)options.GetTypeInfo(typeof(string));
        string payload = null!;

        captured.WriteJsonPanelTrimSafe("Null", payload, typeInfo, CiMode.Off);

        await Assert.That(captured.Output).Contains("custom-null");
    }

    [Test]
    public async Task WriteJsonPanel_null_ciMode_keeps_existing_call_shape()
    {
        var captured = new TestConsole { Profile = { Width = 200 } };

        captured.WriteJsonPanel("Config", new Dictionary<string, bool> { ["Verbose"] = true }, null);

        await Assert.That(captured.Output).Contains("Verbose");
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
    public async Task MaskedValuePatterns_masks_secret_in_exception_text()
    {
        const string Secret = "ghp_abcd1234";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException($"Request failed with {Secret}"), "operation failed");
        }, o =>
        {
            o.MaskedValuePatterns.Add(@"ghp_\w+");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("Request failed with ***");
        await Assert.That(output).DoesNotContain(Secret);
    }

    [Test]
    public async Task Default_value_pattern_masks_ANSI_interleaved_secret_in_exception()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var interleavedSecret = secret.Insert(20, "\x1b[31m");
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException($"Request failed with {interleavedSecret}"), "operation failed");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("Request failed with ***");
        await Assert.That(output).DoesNotContain(secret);
    }

    [Test]
    public async Task Default_value_pattern_masks_backspace_obfuscated_secret_in_exception()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var obfuscatedSecret = secret.Insert(20, "X\b");
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException($"Request failed with {obfuscatedSecret}"), "operation failed");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("Request failed with ***");
        await Assert.That(output).DoesNotContain("ghp_");
    }

    [Test]
    public async Task Default_value_pattern_masks_ANSI_and_backspace_obfuscated_secret_in_exception()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var obfuscatedSecret = "\x1b[0m" + secret.Insert(20, "X\b");
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException($"Request failed with {obfuscatedSecret}"), "operation failed");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("Request failed with ***");
        await Assert.That(output).DoesNotContain("ghp_");
    }

    [Test]
    public async Task Cursor_movement_redacts_exception()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var overwriteMessage = secret.Insert(20, "X\x1b[1D");
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(overwriteMessage), "operation failed");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("ghp_");
    }

    [Test]
    public async Task Rendered_exception_pattern_masks_ANSI_interleaved_secret()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var interleavedSecret = secret.Insert(20, "\x1b[31m");
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException($"Request failed with {interleavedSecret}"), "operation failed");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(@"^InvalidOperationException: .*ghp_\w+");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("ghp_");
    }

    [Test]
    public async Task Rendered_exception_pattern_respects_ShortenTypes_format()
    {
        const string Secret = "ghp_rendered123";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException($"Request failed with {Secret}"), "operation failed");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(@"^InvalidOperationException: .*ghp_\w+");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(Secret);
    }

    [Test]
    public async Task Masked_exception_value_emits_add_mask_in_github_actions()
    {
        const string Secret = "ghp_abcd1234";
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogError(new InvalidOperationException($"Request failed with {Secret}"), "operation failed");
        }, o =>
        {
            o.MaskedValuePatterns.Add(@"ghp_\w+");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains($"::add-mask::{Secret}");
        await Assert.That(output.Split($"::add-mask::{Secret}", StringSplitOptions.None).Length - 1).IsEqualTo(1);
        await Assert.That(output).DoesNotContain($"Request failed with {Secret}");
    }

    [Test]
    public async Task Anchored_value_pattern_masks_exception_message()
    {
        const string Secret = "Bearer abc.def";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(Secret), "operation failed");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(@"^Bearer\s+\S+$");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(Secret);
    }

    [Test]
    public async Task Whole_message_pattern_wins_over_substring_pattern()
    {
        const string Secret = "Bearer secret-prefix.secret-suffix";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(Secret), "operation failed");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add("secret-prefix");
            o.MaskedValuePatterns.Add(@"^Bearer\s+\S+$");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("secret-suffix");
    }

    [Test]
    public async Task Anchored_value_pattern_masks_CRLF_exception_message()
    {
        const string Secret = "Bearer\r\ncredential-value";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(Secret), "operation failed");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(@"^Bearer\s+\S+$");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("credential-value");
    }

    [Test]
    public async Task Bare_carriage_return_redacts_exception()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var overwriteMessage = secret[27..] + "\r" + secret[..27];
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(overwriteMessage), "operation failed");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("ghp_");
    }

    [Test]
    public async Task Cursor_control_in_stack_trace_redacts_exception()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            try
            {
                ThrowFromCursorControlledFile();
            }
            catch (InvalidOperationException exception)
            {
                logger.LogError(exception, "failure");
            }
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("ghp_");
    }

    [Test]
    public async Task Cursor_obfuscated_exception_secret_emits_add_mask_in_github_actions()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var obfuscatedSecret = secret.Insert(20, "X\x1b[1D");
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogError(new InvalidOperationException(obfuscatedSecret), "failure");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains($"::add-mask::{secret}");
        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(obfuscatedSecret);
    }

    [Test]
    public async Task Delete_character_obfuscated_exception_secret_emits_add_mask_in_github_actions()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var obfuscatedSecret = secret.Insert(20, "X") + "\x1b[21D\x1b[P";
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogError(new InvalidOperationException(obfuscatedSecret), "failure");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains($"::add-mask::{secret}");
        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(obfuscatedSecret);
    }

    [Test]
    public async Task Carriage_overwritten_exception_secret_emits_add_mask_in_github_actions()
    {
        var secret = $"ghp_{new string('a', 36)}";
        var obfuscatedSecret = new string(' ', 20) + secret[20..] + "\r" + secret[..20];
        var output = await LogTestHarness.CaptureAsync(CiMode.GitHubActions, logger =>
        {
            logger.LogError(new InvalidOperationException(obfuscatedSecret), "failure");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains($"::add-mask::{secret}");
        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(obfuscatedSecret);
    }

    [Test]
    public async Task Anchored_value_pattern_masks_inner_exception_message()
    {
        const string Secret = "Bearer inner.secret";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            var inner = new ArgumentException(Secret);
            logger.LogError(new InvalidOperationException("operation failed", inner), "failure");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(@"^Bearer\s+\S+$");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(Secret);
    }

    [Test]
    public async Task Zero_width_value_pattern_masks_exception_message()
    {
        const string Secret = "Bearer zero.width";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(Secret), "failure");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(@"(?=Bearer\s+\S+)");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(Secret);
    }

    [Test]
    public async Task Zero_width_value_pattern_handles_empty_exception_message()
    {
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new Exception(string.Empty), "failure");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(@"^$");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("failure");
        await Assert.That(output).Contains("***");
    }

    [Test]
    public async Task Default_value_pattern_masks_entire_private_key_in_exception()
    {
        const string PrivateKey = "-----BEGIN PRIVATE KEY-----\nYWJjZGVmZ2hpamtsbW5vcA==\n-----END PRIVATE KEY-----";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(PrivateKey), "operation failed");
        }, o => o.Template = "{Message}");

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("YWJjZGVmZ2hpamtsbW5vcA==");
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

    private static string[] GetPhysicalLines(string output) =>
        output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

#line 1 "C:\\src\\ghp_aaaaaaaaaaaaaaaaX[1Daaaaaaaaaaaaaaaaaaaa.cs"
    private static void ThrowFromCursorControlledFile() => throw new InvalidOperationException("operation failed");
#line default

    private static async Task<(string Output, int ConsoleWidth)> CaptureAtWidthAsync(
        CiMode mode,
        int width,
        string message,
        Action<SpectreConsoleLoggerOptions>? configure = null)
    {
        var (console, services, logger) = LogTestHarness.Build(mode, o =>
        {
            o.Template = "{Message}";
            configure?.Invoke(o);
        });
        console.Profile.Width = width;

        try
        {
            logger.LogInformation("{Message}", message);
        }
        finally
        {
            await services.DisposeAsync();
        }

        return (console.Output, console.Profile.Width);
    }

    private sealed class NullStringConverter : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value ?? "custom-null");
    }

    [Test]
    public async Task Overlapping_outer_and_inner_exception_patterns_mask_as_one_range_set()
    {
        const string InnerSecret = "Bearer secret-prefix.secret-suffix";
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            var inner = new ArgumentException(InnerSecret);
            logger.LogError(new InvalidOperationException("secret-prefix", inner), "failure");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add("secret-prefix");
            o.MaskedValuePatterns.Add(@"^Bearer\s+\S+$");
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain("secret-suffix");
    }

    [Test]
    public async Task Exception_masking_does_not_wrap_at_fixed_scan_width()
    {
        const string Secret = "secret-value";
        var message = new string('x', 999_990) + Secret;
        var output = await LogTestHarness.CaptureAsync(CiMode.Off, logger =>
        {
            logger.LogError(new InvalidOperationException(message), "failure");
        }, o =>
        {
            o.MaskedValuePatterns.Clear();
            o.MaskedValuePatterns.Add(Secret);
            o.Template = "{Message}";
        });

        await Assert.That(output).Contains("***");
        await Assert.That(output).DoesNotContain(Secret);
    }
}
