using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MEL.Spectre.Templates;

namespace MEL.Spectre;

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
            catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
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

        if (options.BackpressureMode == BackpressureMode.Wait && options.EnqueueWaitTimeout == TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.EnqueueWaitTimeout)} must be greater than zero when BackpressureMode is Wait; use DropNewest or DropOldest for non-blocking semantics.");
        }

        if (options.Theme is null)
        {
            failures.Add($"{nameof(options.Theme)} must not be null.");
        }

        for (var i = 0; i < options.MaskedNamePatterns.Count; i++)
        {
            var pattern = options.MaskedNamePatterns[i];
            if (pattern is null)
            {
                failures.Add($"{nameof(options.MaskedNamePatterns)}[{i}] must not be null.");
                continue;
            }
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (Exception ex) when (!FatalExceptions.IsFatal(ex))
            {
                failures.Add($"{nameof(options.MaskedNamePatterns)}[{i}] is not a valid regex: {ex.Message}");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
