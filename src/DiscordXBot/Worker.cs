using DiscordXBot.Configuration;
using DiscordXBot.Data;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services;
using DiscordXBot.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordXBot;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PollingOptions> _pollingOptions;
    private readonly IOptionsMonitor<RetryOptions> _retryOptions;
    private readonly RssBridgeClient _rssBridgeClient;
    private readonly TweetContentParser _tweetContentParser;
    private readonly DiscordPublisher _discordPublisher;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PollingOptions> pollingOptions,
        IOptionsMonitor<RetryOptions> retryOptions,
        RssBridgeClient rssBridgeClient,
        TweetContentParser tweetContentParser,
        DiscordPublisher discordPublisher)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _pollingOptions = pollingOptions;
        _retryOptions = retryOptions;
        _rssBridgeClient = rssBridgeClient;
        _tweetContentParser = tweetContentParser;
        _discordPublisher = discordPublisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = Math.Max(1, _pollingOptions.CurrentValue.IntervalMinutes);

            try
            {
                await ProcessFeedsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled polling error.");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task ProcessFeedsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var trackedFeeds = await db.TrackedFeeds
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);

        if (trackedFeeds.Count == 0)
        {
            _logger.LogInformation("Polling cycle finished: no tracked feeds found.");
            return;
        }

        var totalPublished = 0;
        var groupedFeeds = trackedFeeds
            .GroupBy(x => x.XUsername, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groupedFeeds)
        {
            var sampleFeed = group.First();
            IReadOnlyList<RssPost> posts;

            try
            {
                posts = await FetchPostsWithRetryAsync(
                    sampleFeed.RssUrl,
                    _pollingOptions.CurrentValue.MaxItemsPerFeed,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch RSS for @{Username}", sampleFeed.XUsername);
                continue;
            }

            if (posts.Count == 0)
            {
                continue;
            }

            foreach (var post in posts.OrderBy(x => x.PublishedAtUtc))
            {
                var parsed = _tweetContentParser.Parse(post);

                foreach (var feed in group)
                {
                    var alreadyProcessed = await db.ProcessedTweets
                        .AsNoTracking()
                        .AnyAsync(
                            x => x.TrackedFeedId == feed.Id && x.TweetId == post.TweetId,
                            cancellationToken);

                    if (alreadyProcessed)
                    {
                        continue;
                    }

                    var published = await _discordPublisher.PublishAsync(feed, post, parsed, cancellationToken);
                    if (!published)
                    {
                        continue;
                    }

                    var processed = new ProcessedTweet
                    {
                        TrackedFeedId = feed.Id,
                        GuildId = feed.GuildId,
                        ChannelId = feed.ChannelId,
                        XUsername = feed.XUsername,
                        TweetId = post.TweetId,
                        TweetUrl = post.Url,
                        ProcessedAtUtc = DateTime.UtcNow
                    };

                    db.ProcessedTweets.Add(processed);

                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        totalPublished++;
                    }
                    catch (DbUpdateException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Skipping duplicate persistence for tweet {TweetId} and feed {FeedId}",
                            post.TweetId,
                            feed.Id);

                        db.Entry(processed).State = EntityState.Detached;
                    }
                }
            }
        }

        _logger.LogInformation(
            "Polling cycle done. Feeds: {FeedCount}, Groups: {GroupCount}, Published: {PublishedCount}",
            trackedFeeds.Count,
            groupedFeeds.Count,
            totalPublished);
    }

    private async Task<IReadOnlyList<RssPost>> FetchPostsWithRetryAsync(
        string rssUrl,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _retryOptions.CurrentValue.MaxRetries);
        var initialDelaySeconds = Math.Max(1, _retryOptions.CurrentValue.InitialDelaySeconds);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await _rssBridgeClient.GetPostsAsync(rssUrl, maxItems, cancellationToken);
            }
            catch when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken);
            }
        }

        return [];
    }
}
