using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Spectre.MEL.Templates;

namespace Spectre.MEL;

internal sealed class SpectreConsoleLoggerOptionsValidator : IValidateOptions<SpectreConsoleLoggerOptions>
{
    public ValidateOptionsResult Validate(string? name, SpectreConsoleLoggerOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Template))
        {
            failures.Add($"{nameof(options.Template)} must be non-empty.");
        }
        else
        {
            try
            {
                _ = new OutputTemplate(options.Template);
            }
            catch (FormatException ex)
            {
                failures.Add($"{nameof(options.Template)} is not a valid template: {ex.Message}");
            }
        }

        if (options.ChannelCapacity <= 0)
        {
            failures.Add($"{nameof(options.ChannelCapacity)} must be greater than 0.");
        }

        if (options.MaskedValueCacheCapacity < 0)
        {
            failures.Add($"{nameof(options.MaskedValueCacheCapacity)} must be greater than or equal to 0.");
        }

        if (options.ShutdownDrainTimeout < TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.ShutdownDrainTimeout)} must be non-negative.");
        }

        if (options.EnqueueWaitTimeout < TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.EnqueueWaitTimeout)} must be non-negative.");
        }

        if (options.EnqueueWaitTimeout > options.ShutdownDrainTimeout && options.ShutdownDrainTimeout > TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.EnqueueWaitTimeout)} must not exceed {nameof(options.ShutdownDrainTimeout)}.");
        }

        if (options.Theme is null)
        {
            failures.Add($"{nameof(options.Theme)} must not be null.");
        }

        for (var i = 0; i < options.MaskedNamePatterns.Count; i++)
        {
            try
            {
                _ = new Regex(options.MaskedNamePatterns[i], RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                failures.Add($"{nameof(options.MaskedNamePatterns)}[{i}] is not a valid regex: {ex.Message}");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
