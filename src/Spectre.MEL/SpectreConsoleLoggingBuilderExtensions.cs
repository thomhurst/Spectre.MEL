using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Spectre.MEL.Provider;

namespace Spectre.MEL;

public static class SpectreConsoleLoggingBuilderExtensions
{
    public static ILoggingBuilder AddSpectreConsole(this ILoggingBuilder builder, Action<SpectreConsoleLoggerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<SpectreConsoleLoggerOptions>()
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SpectreConsoleLoggerOptions>, SpectreConsoleLoggerOptionsValidator>());
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        for (var i = builder.Services.Count - 1; i >= 0; i--)
        {
            if (builder.Services[i].ImplementationType == typeof(ConsoleLoggerProvider))
            {
                builder.Services.RemoveAt(i);
            }
        }

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, SpectreConsoleLoggerProvider>());

        return builder;
    }
}
