using DiscordXBot.Configuration;
using Microsoft.Extensions.Options;

namespace DiscordXBot;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<PollingOptions> _pollingOptions;

    public Worker(ILogger<Worker> logger, IOptionsMonitor<PollingOptions> pollingOptions)
    {
        _logger = logger;
        _pollingOptions = pollingOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = Math.Max(1, _pollingOptions.CurrentValue.IntervalMinutes);
            _logger.LogInformation(
                "Bootstrap worker heartbeat at {time}. Next cycle in {intervalMinutes} minute(s)",
                DateTimeOffset.UtcNow,
                intervalMinutes);

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
