using Microsoft.Extensions.Options;

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

        if (options.Theme is null)
        {
            failures.Add($"{nameof(options.Theme)} must not be null.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
