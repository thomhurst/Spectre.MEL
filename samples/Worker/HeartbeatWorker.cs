using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MEL.Spectre.Samples.Worker;

internal sealed partial class HeartbeatWorker(ILogger<HeartbeatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using (logger.BeginScope("Tick {Tick}", tick))
            {
                LogHeartbeat(tick, DateTimeOffset.UtcNow);
                if (tick % 5 == 0 && tick > 0)
                {
                    LogBackpressure(tick * 12);
                }
            }

            tick++;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Heartbeat {Tick} at {Timestamp:o}")]
    private partial void LogHeartbeat(int tick, DateTimeOffset timestamp);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Queue depth high: {Depth}")]
    private partial void LogBackpressure(int depth);
}
