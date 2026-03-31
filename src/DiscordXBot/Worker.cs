using DiscordXBot.Configuration;
using DiscordXBot.Data;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services;
using DiscordXBot.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DiscordXBot;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PollingOptions> _pollingOptions;
    private readonly IOptionsMonitor<RetryOptions> _retryOptions;
    private readonly IOptionsMonitor<PublishOptions> _publishOptions;
    private readonly RssBridgeClient _rssBridgeClient;
    private readonly TweetContentParser _tweetContentParser;
    private readonly DiscordPublisher _discordPublisher;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PollingOptions> pollingOptions,
        IOptionsMonitor<RetryOptions> retryOptions,
        IOptionsMonitor<PublishOptions> publishOptions,
        RssBridgeClient rssBridgeClient,
        TweetContentParser tweetContentParser,
        DiscordPublisher discordPublisher)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _pollingOptions = pollingOptions;
        _retryOptions = retryOptions;
        _publishOptions = publishOptions;
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
        var duplicateSkips = 0;
        var publishFailures = 0;
        var totalRetries = 0;
        var groupedFeeds = trackedFeeds
            .GroupBy(x => x.XUsername, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var publishLimiter = new SemaphoreSlim(Math.Max(1, _publishOptions.CurrentValue.MaxConcurrentPublishes));
        var interPublishDelayMs = Math.Max(0, _publishOptions.CurrentValue.InterPublishDelayMs);

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
                        duplicateSkips++;
                        continue;
                    }

                    var publishResult = await PublishWithRetryAsync(
                        publishLimiter,
                        feed,
                        post,
                        parsed,
                        cancellationToken);

                    totalRetries += publishResult.RetryCount;

                    if (!publishResult.Result.Success)
                    {
                        publishFailures++;
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

                        if (interPublishDelayMs > 0)
                        {
                            await Task.Delay(interPublishDelayMs, cancellationToken);
                        }
                    }
                    catch (DbUpdateException ex)
                    {
                        if (IsUniqueViolation(ex))
                        {
                            duplicateSkips++;
                            _logger.LogInformation(
                                ex,
                                "Duplicate marker detected for tweet {TweetId} and feed {FeedId}",
                                post.TweetId,
                                feed.Id);
                        }
                        else
                        {
                            publishFailures++;
                            _logger.LogError(
                                ex,
                                "Failed persisting ProcessedTweet for tweet {TweetId} and feed {FeedId}",
                                post.TweetId,
                                feed.Id);
                        }

                        db.Entry(processed).State = EntityState.Detached;
                    }
                }
            }
        }

        _logger.LogInformation(
            "Polling cycle done. Feeds: {FeedCount}, Groups: {GroupCount}, Published: {PublishedCount}, Duplicates: {DuplicateCount}, Failures: {FailureCount}, Retries: {RetryCount}",
            trackedFeeds.Count,
            groupedFeeds.Count,
            totalPublished,
            duplicateSkips,
            publishFailures,
            totalRetries);
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

    private async Task<(PublishResult Result, int RetryCount)> PublishWithRetryAsync(
        SemaphoreSlim limiter,
        TrackedFeed feed,
        RssPost post,
        ParsedTweetContent parsed,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _retryOptions.CurrentValue.PublishMaxRetries);
        var initialDelaySeconds = Math.Max(1, _retryOptions.CurrentValue.InitialDelaySeconds);
        var retries = 0;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                var result = await _discordPublisher.PublishAsync(feed, post, parsed, cancellationToken);
                if (result.Success)
                {
                    return (result, retries);
                }

                if (!IsRetriableFailure(result.FailureKind) || attempt >= maxRetries)
                {
                    return (result, retries);
                }
            }
            finally
            {
                limiter.Release();
            }

            retries++;
            var delay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, attempt));
            await Task.Delay(delay, cancellationToken);
        }

        return (PublishResult.Fail(PublishFailureKind.Fatal, "Retry budget exhausted"), retries);
    }

    private static bool IsRetriableFailure(PublishFailureKind failureKind)
    {
        return failureKind is PublishFailureKind.RateLimited or PublishFailureKind.Transient;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: "23505" };
    }
}
