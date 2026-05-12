using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MEL.Spectre;

using var sp = new ServiceCollection()
    .AddLogging(builder => builder
        .SetMinimumLevel(LogLevel.Trace)
        .AddSpectreConsole(options =>
        {
            options.IncludeScopes = true;
        }))
    .BuildServiceProvider();

var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("MEL.Spectre.Sample");

logger.LogTrace("Boot sequence start");
logger.LogDebug("Configuration loaded from {Path}", "/etc/app/config.yaml");
logger.LogInformation("User {UserId} signed in with email {Email}", 42, "ada@example.com");
logger.LogInformation("Request finished in {Elapsed}ms with status {StatusCode}", 87.4, 200);
logger.LogWarning("Cache hit rate dropped to {Rate}", 0.42);

using (logger.BeginScope("Database batch {BatchId}", Guid.NewGuid()))
{
    logger.LogInformation("Inserted {Count} rows", 128);

    using (logger.BeginScope("Retry attempt {Attempt}", 2))
    {
        logger.LogWarning("Transient failure: {Reason}", "deadlock detected");
    }

    logger.LogInformation("Committed transaction in {Duration}ms", 12.7);
}

logger.LogInformation("Auth header was {Authorization}", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
logger.LogInformation("Stored API key {ApiKey} for tenant {TenantId}", "sk_live_abc123", "acme");

try
{
    throw new InvalidOperationException("Simulated explosion to demonstrate exception rendering");
}
catch (Exception ex)
{
    logger.LogError(ex, "Operation {Operation} failed", "ProcessOrder");
}

await sp.DisposeAsync();
