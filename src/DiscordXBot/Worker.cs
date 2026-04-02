using DiscordXBot.Configuration;
using DiscordXBot.Data;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services;
using DiscordXBot.Services.Models;
using System.Collections.Concurrent;
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
    private readonly IOptionsMonitor<FeedProviderOptions> _feedProviderOptions;
    private readonly IOptionsMonitor<RssBridgeFallbackOptions> _rssBridgeFallbackOptions;
    private readonly IOptionsMonitor<ApifyFallbackOptions> _apifyFallbackOptions;
    private readonly RssBridgeClient _rssBridgeClient;
    private readonly FeedUrlResolver _feedUrlResolver;
    private readonly ApifyFacebookClient _apifyFacebookClient;
    private readonly TweetContentParser _tweetContentParser;
    private readonly DiscordPublisher _discordPublisher;
    private readonly ConcurrentDictionary<string, ProfileFetchHealthState> _profileFetchHealth = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FacebookFallbackState> _rssBridgeFallbackState = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FacebookFallbackState> _apifyFallbackState = new(StringComparer.OrdinalIgnoreCase);

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PollingOptions> pollingOptions,
        IOptionsMonitor<RetryOptions> retryOptions,
        IOptionsMonitor<PublishOptions> publishOptions,
        IOptionsMonitor<FeedProviderOptions> feedProviderOptions,
        IOptionsMonitor<RssBridgeFallbackOptions> rssBridgeFallbackOptions,
        RssBridgeClient rssBridgeClient,
        FeedUrlResolver feedUrlResolver,
        IOptionsMonitor<ApifyFallbackOptions> apifyFallbackOptions,
        ApifyFacebookClient apifyFacebookClient,
        TweetContentParser tweetContentParser,
        DiscordPublisher discordPublisher)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _pollingOptions = pollingOptions;
        _retryOptions = retryOptions;
        _publishOptions = publishOptions;
        _feedProviderOptions = feedProviderOptions;
        _rssBridgeFallbackOptions = rssBridgeFallbackOptions;
        _apifyFallbackOptions = apifyFallbackOptions;
        _rssBridgeClient = rssBridgeClient;
        _feedUrlResolver = feedUrlResolver;
        _apifyFacebookClient = apifyFacebookClient;
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
        var mediaFilteredSkips = 0;
        var publishFailures = 0;
        var totalRetries = 0;
        var groupedFeeds = trackedFeeds
            .GroupBy(x => new
            {
                x.Platform,
                x.SourceType,
                SourceKey = GetSourceKey(x)
            })
            .ToList();

        using var publishLimiter = new SemaphoreSlim(Math.Max(1, _publishOptions.CurrentValue.MaxConcurrentPublishes));
        var interPublishDelayMs = Math.Max(0, _publishOptions.CurrentValue.InterPublishDelayMs);

        foreach (var group in groupedFeeds)
        {
            var sampleFeed = group.First();
            var feedLabel = GetFeedLabel(sampleFeed);
            FeedFetchResult fetchResult;
            FeedFetchResult effectiveFetchResult;

            try
            {
                fetchResult = await FetchPostsWithRetryAsync(
                    sampleFeed,
                    _pollingOptions.CurrentValue.MaxItemsPerFeed,
                    cancellationToken);

                var rssBridgeFallbackResult = await TryApplyFacebookRssBridgeFallbackAsync(
                    sampleFeed,
                    fetchResult,
                    _pollingOptions.CurrentValue.MaxItemsPerFeed,
                    cancellationToken);

                effectiveFetchResult = await TryApplyFacebookApifyFallbackAsync(
                    sampleFeed,
                    rssBridgeFallbackResult,
                    _pollingOptions.CurrentValue.MaxItemsPerFeed,
                    cancellationToken);

                await EvaluateProfileHealthAsync(
                    db,
                    sampleFeed,
                    effectiveFetchResult,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch RSS for {FeedLabel}", feedLabel);
                continue;
            }

            var posts = effectiveFetchResult.Posts;

            if (posts.Count == 0)
            {
                continue;
            }

            foreach (var post in posts.OrderBy(x => x.PublishedAtUtc))
            {
                var parsed = _tweetContentParser.Parse(post, sampleFeed.Platform);

                if (!IsAllowedByMediaPolicy(sampleFeed, parsed.MediaType))
                {
                    mediaFilteredSkips += group.Count();
                    _logger.LogInformation(
                        "Skipping post {PostId} for source {FeedLabel}: media type {MediaType} is blocked by media policy.",
                        post.TweetId,
                        feedLabel,
                        parsed.MediaType);
                    continue;
                }

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
            "Polling cycle done. Feeds: {FeedCount}, Groups: {GroupCount}, Published: {PublishedCount}, Duplicates: {DuplicateCount}, MediaFiltered: {MediaFilteredCount}, Failures: {FailureCount}, Retries: {RetryCount}",
            trackedFeeds.Count,
            groupedFeeds.Count,
            totalPublished,
            duplicateSkips,
            mediaFilteredSkips,
            publishFailures,
            totalRetries);
    }

    private async Task<FeedFetchResult> FetchPostsWithRetryAsync(
        TrackedFeed trackedFeed,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _retryOptions.CurrentValue.MaxRetries);
        var initialDelaySeconds = Math.Max(1, _retryOptions.CurrentValue.InitialDelaySeconds);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var result = await _rssBridgeClient.GetPostsDetailedAsync(trackedFeed, maxItems, cancellationToken);
            if (!IsRetriableFetchOutcome(result) || attempt >= maxRetries)
            {
                return result;
            }

            var delay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, attempt));
            await Task.Delay(delay, cancellationToken);
        }

        return FeedFetchResult.NetworkError("Fetch retry budget exhausted.");
    }

    private async Task<FeedFetchResult> TryApplyFacebookRssBridgeFallbackAsync(
        TrackedFeed sampleFeed,
        FeedFetchResult primaryResult,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (sampleFeed.Platform != FeedPlatform.Facebook)
        {
            return primaryResult;
        }

        var stateKey = GetRssBridgeFallbackStateKey(sampleFeed);
        var state = _rssBridgeFallbackState.GetOrAdd(stateKey, _ => new FacebookFallbackState());

        if (primaryResult.Outcome == FeedFetchOutcome.Success && primaryResult.Posts.Count > 0)
        {
            state.ConsecutivePrimaryFailures = 0;
            return primaryResult;
        }

        if (!ShouldConsiderFallbackOutcome(primaryResult.Outcome))
        {
            return primaryResult;
        }

        var options = _rssBridgeFallbackOptions.CurrentValue;
        if (!options.Enabled)
        {
            return primaryResult;
        }

        if ((sampleFeed.SourceType == FacebookSourceType.Fanpage && !options.EnableForFanpage) ||
            (sampleFeed.SourceType == FacebookSourceType.Profile && !options.EnableForProfile))
        {
            return primaryResult;
        }

        if (sampleFeed.Provider == FeedProvider.RssBridge)
        {
            return primaryResult;
        }

        state.ConsecutivePrimaryFailures++;

        var threshold = Math.Max(1, options.FailureThreshold);
        if (state.ConsecutivePrimaryFailures < threshold)
        {
            return primaryResult;
        }

        var cooldownMinutes = Math.Max(5, options.CooldownMinutes);
        var utcNow = DateTime.UtcNow;
        if (state.LastFallbackAttemptUtc != default &&
            utcNow - state.LastFallbackAttemptUtc < TimeSpan.FromMinutes(cooldownMinutes))
        {
            return primaryResult;
        }

        string fallbackUrl;
        try
        {
            var source = string.IsNullOrWhiteSpace(sampleFeed.SourceKey)
                ? sampleFeed.XUsername
                : sampleFeed.SourceKey;

            fallbackUrl = _feedUrlResolver.Resolve(
                FeedPlatform.Facebook,
                FeedProvider.RssBridge,
                source,
                sampleFeed.SourceType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "RSS-Bridge fallback cannot resolve source {FeedLabel}.",
                GetFeedLabel(sampleFeed));
            return primaryResult;
        }

        var fallbackFeed = new TrackedFeed
        {
            Platform = FeedPlatform.Facebook,
            SourceType = sampleFeed.SourceType,
            SourceKey = sampleFeed.SourceKey,
            XUsername = sampleFeed.XUsername,
            Provider = FeedProvider.RssBridge,
            RssUrl = fallbackUrl,
            IsActive = sampleFeed.IsActive,
            GuildId = sampleFeed.GuildId,
            ChannelId = sampleFeed.ChannelId
        };

        state.LastFallbackAttemptUtc = utcNow;

        var fallbackResult = await _rssBridgeClient.GetPostsDetailedAsync(fallbackFeed, maxItems, cancellationToken);
        if (fallbackResult.Outcome == FeedFetchOutcome.Success && fallbackResult.Posts.Count > 0)
        {
            state.ConsecutivePrimaryFailures = 0;

            _logger.LogInformation(
                "Using RSS-Bridge fallback for source {FeedLabel}. PrimaryOutcome={PrimaryOutcome}, FallbackPosts={Count}",
                GetFeedLabel(sampleFeed),
                GetFetchOutcomeLabel(primaryResult.Outcome),
                fallbackResult.Posts.Count);

            return fallbackResult;
        }

        _logger.LogWarning(
            "RSS-Bridge fallback returned no usable posts for source {FeedLabel}. PrimaryOutcome={PrimaryOutcome}, FallbackOutcome={FallbackOutcome}",
            GetFeedLabel(sampleFeed),
            GetFetchOutcomeLabel(primaryResult.Outcome),
            GetFetchOutcomeLabel(fallbackResult.Outcome));

        return primaryResult;
    }

    private async Task<FeedFetchResult> TryApplyFacebookApifyFallbackAsync(
        TrackedFeed sampleFeed,
        FeedFetchResult primaryResult,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (sampleFeed.Platform != FeedPlatform.Facebook)
        {
            return primaryResult;
        }

        var stateKey = GetApifyFallbackStateKey(sampleFeed);
        var state = _apifyFallbackState.GetOrAdd(stateKey, _ => new FacebookFallbackState());

        if (primaryResult.Outcome == FeedFetchOutcome.Success && primaryResult.Posts.Count > 0)
        {
            state.ConsecutivePrimaryFailures = 0;
            return primaryResult;
        }

        if (!ShouldConsiderFallbackOutcome(primaryResult.Outcome))
        {
            return primaryResult;
        }

        var options = _apifyFallbackOptions.CurrentValue;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiToken))
        {
            return primaryResult;
        }

        if ((sampleFeed.SourceType == FacebookSourceType.Fanpage && !options.EnableForFanpage) ||
            (sampleFeed.SourceType == FacebookSourceType.Profile && !options.EnableForProfile))
        {
            return primaryResult;
        }

        state.ConsecutivePrimaryFailures++;

        var threshold = Math.Max(1, options.FailureThreshold);
        if (state.ConsecutivePrimaryFailures < threshold)
        {
            return primaryResult;
        }

        var cooldownMinutes = Math.Max(5, options.CooldownMinutes);
        var utcNow = DateTime.UtcNow;
        if (state.LastFallbackAttemptUtc != default &&
            utcNow - state.LastFallbackAttemptUtc < TimeSpan.FromMinutes(cooldownMinutes))
        {
            return primaryResult;
        }

        state.LastFallbackAttemptUtc = utcNow;
        var fallbackPosts = await _apifyFacebookClient.FetchPostsAsync(sampleFeed, maxItems, cancellationToken);
        if (fallbackPosts.Count == 0)
        {
            _logger.LogWarning(
                "Apify fallback returned no posts for source {FeedLabel}. PrimaryOutcome={PrimaryOutcome}",
                GetFeedLabel(sampleFeed),
                GetFetchOutcomeLabel(primaryResult.Outcome));
            return primaryResult;
        }

        state.ConsecutivePrimaryFailures = 0;

        _logger.LogInformation(
            "Using Apify fallback for source {FeedLabel}. PrimaryOutcome={PrimaryOutcome}, FallbackPosts={Count}",
            GetFeedLabel(sampleFeed),
            GetFetchOutcomeLabel(primaryResult.Outcome),
            fallbackPosts.Count);

        return FeedFetchResult.Success(fallbackPosts);
    }

    private static bool ShouldConsiderFallbackOutcome(FeedFetchOutcome outcome)
    {
        return outcome is FeedFetchOutcome.HttpForbidden
            or FeedFetchOutcome.HttpError
            or FeedFetchOutcome.NetworkError
            or FeedFetchOutcome.ParseError
            or FeedFetchOutcome.ErrorOnly;
    }

    private async Task EvaluateProfileHealthAsync(
        BotDbContext db,
        TrackedFeed sampleFeed,
        FeedFetchResult fetchResult,
        CancellationToken cancellationToken)
    {
        if (sampleFeed.Platform != FeedPlatform.Facebook || sampleFeed.SourceType != FacebookSourceType.Profile)
        {
            return;
        }

        var stateKey = GetProfileStateKey(sampleFeed);
        var state = _profileFetchHealth.GetOrAdd(stateKey, _ => new ProfileFetchHealthState());

        if (fetchResult.Outcome == FeedFetchOutcome.Success && fetchResult.Posts.Count > 0)
        {
            state.ConsecutiveFailures = 0;
            state.HasHistoricalSuccess = true;
            return;
        }

        var shouldCountFailure = await ShouldCountProfileFailureAsync(
            db,
            sampleFeed,
            fetchResult,
            state,
            cancellationToken);

        if (!shouldCountFailure)
        {
            state.ConsecutiveFailures = 0;
            return;
        }

        state.ConsecutiveFailures++;
        var outcomeLabel = GetFetchOutcomeLabel(fetchResult.Outcome);

        _logger.LogWarning(
            "Profile feed issue detected for {FeedLabel}. ConsecutiveIssues={ConsecutiveIssues}, Outcome={Outcome}, StatusCode={StatusCode}",
            GetFeedLabel(sampleFeed),
            state.ConsecutiveFailures,
            outcomeLabel,
            fetchResult.StatusCode);

        var options = _feedProviderOptions.CurrentValue;
        if (!options.EnableFacebookProfileAlerts || options.FacebookProfileAlertChannelId == 0)
        {
            return;
        }

        var failureThreshold = Math.Max(1, options.FacebookProfileFailureThreshold);
        if (state.ConsecutiveFailures < failureThreshold)
        {
            return;
        }

        var cooldownMinutes = Math.Max(5, options.FacebookProfileAlertCooldownMinutes);
        var utcNow = DateTime.UtcNow;
        if (state.LastAlertAtUtc != default &&
            utcNow - state.LastAlertAtUtc < TimeSpan.FromMinutes(cooldownMinutes))
        {
            return;
        }

        var alertMessage =
            $"[FB Profile Alert] Source={sampleFeed.SourceKey} has {state.ConsecutiveFailures} consecutive fetch issues ({outcomeLabel}). " +
            "Cookie may be expired or profile visibility changed. Check RSSHub FB_COOKIE and profile route /facebook/user/{id}.";

        var alertResult = await _discordPublisher.PublishSystemAlertAsync(
            options.FacebookProfileAlertChannelId,
            alertMessage,
            cancellationToken);

        if (alertResult.Success)
        {
            state.LastAlertAtUtc = utcNow;
            _logger.LogInformation(
                "Sent FB profile health alert for source {SourceKey} to channel {ChannelId}",
                sampleFeed.SourceKey,
                options.FacebookProfileAlertChannelId);
            return;
        }

        _logger.LogWarning(
            "Failed to send FB profile health alert for source {SourceKey}: {Failure}",
            sampleFeed.SourceKey,
            alertResult.ErrorMessage);
    }

    private async Task<bool> ShouldCountProfileFailureAsync(
        BotDbContext db,
        TrackedFeed sampleFeed,
        FeedFetchResult fetchResult,
        ProfileFetchHealthState state,
        CancellationToken cancellationToken)
    {
        if (fetchResult.Outcome is FeedFetchOutcome.HttpForbidden or FeedFetchOutcome.ErrorOnly)
        {
            return true;
        }

        if (fetchResult.Outcome != FeedFetchOutcome.Empty)
        {
            return false;
        }

        if (state.HasHistoricalSuccess)
        {
            return true;
        }

        var hasHistoricalPublish = await db.ProcessedTweets
            .AsNoTracking()
            .AnyAsync(x => x.TrackedFeedId == sampleFeed.Id, cancellationToken);

        if (hasHistoricalPublish)
        {
            state.HasHistoricalSuccess = true;
        }

        return hasHistoricalPublish;
    }

    private static bool IsRetriableFetchOutcome(FeedFetchResult result)
    {
        if (result.Outcome == FeedFetchOutcome.NetworkError)
        {
            return true;
        }

        return result.Outcome == FeedFetchOutcome.HttpError && result.StatusCode is >= 500;
    }

    private static string GetFetchOutcomeLabel(FeedFetchOutcome outcome)
    {
        return outcome switch
        {
            FeedFetchOutcome.HttpForbidden => "HTTP 403",
            FeedFetchOutcome.ErrorOnly => "error-only-feed",
            FeedFetchOutcome.Empty => "empty-feed",
            FeedFetchOutcome.HttpError => "http-error",
            FeedFetchOutcome.NetworkError => "network-error",
            FeedFetchOutcome.ParseError => "parse-error",
            _ => "success"
        };
    }

    private static string GetProfileStateKey(TrackedFeed feed)
    {
        var source = GetSourceKey(feed);
        return $"{feed.Platform}:{feed.SourceType}:{source}";
    }

    private static string GetRssBridgeFallbackStateKey(TrackedFeed feed)
    {
        var source = GetSourceKey(feed);
        return $"fb-rssbridge-fallback:{feed.SourceType}:{source}";
    }

    private static string GetApifyFallbackStateKey(TrackedFeed feed)
    {
        var source = GetSourceKey(feed);
        return $"fb-apify-fallback:{feed.SourceType}:{source}";
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

    private static bool IsAllowedByMediaPolicy(TrackedFeed feed, ParsedMediaType mediaType)
    {
        if (feed.Platform == FeedPlatform.X)
        {
            // Keep existing strict behavior for X feeds.
            return mediaType == ParsedMediaType.ImageOnly;
        }

        // Fanpage/direct RSS paths can publish caption and mixed posts.
        return mediaType is ParsedMediaType.ImageOnly or ParsedMediaType.CaptionOnly or ParsedMediaType.Mixed;
    }

    private static string GetSourceKey(TrackedFeed feed)
    {
        return (feed.SourceKey ?? feed.XUsername ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string GetFeedLabel(TrackedFeed feed)
    {
        var sourceKey = string.IsNullOrWhiteSpace(feed.SourceKey) ? feed.XUsername : feed.SourceKey;
        if (feed.Platform == FeedPlatform.Facebook)
        {
            return $"{feed.Platform}:{feed.SourceType}:{sourceKey}";
        }

        return $"{feed.Platform}:{sourceKey}";
    }

    private sealed class ProfileFetchHealthState
    {
        public int ConsecutiveFailures { get; set; }
        public bool HasHistoricalSuccess { get; set; }
        public DateTime LastAlertAtUtc { get; set; }
    }

    private sealed class FacebookFallbackState
    {
        public int ConsecutivePrimaryFailures { get; set; }
        public DateTime LastFallbackAttemptUtc { get; set; }
    }
}
