using System.ServiceModel.Syndication;
using System.Xml;
using DiscordXBot.Configuration;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services.Models;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Services;

public sealed class RssBridgeClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<RssBridgeOptions> rssBridgeOptions,
    ILogger<RssBridgeClient> logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptionsMonitor<RssBridgeOptions> _rssBridgeOptions = rssBridgeOptions;
    private readonly ILogger<RssBridgeClient> _logger = logger;

    public Task<IReadOnlyList<RssPost>> GetPostsAsync(string rssUrl, int maxItems, CancellationToken cancellationToken)
    {
        var legacyFeed = new TrackedFeed
        {
            RssUrl = rssUrl,
            Platform = FeedPlatform.X,
            Provider = FeedProvider.RssBridge,
            SourceKey = string.Empty
        };

        return GetPostsAsync(legacyFeed, maxItems, cancellationToken);
    }

    public async Task<IReadOnlyList<RssPost>> GetPostsAsync(TrackedFeed trackedFeed, int maxItems, CancellationToken cancellationToken)
    {
        var rssUrl = trackedFeed.RssUrl;
        var client = _httpClientFactory.CreateClient();

        using var response = await client.GetAsync(rssUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = false });
        var feed = SyndicationFeed.Load(reader);

        if (feed?.Items is null)
        {
            return [];
        }

        var filteredItems = feed.Items
            .Where(item => !IsBridgeErrorItem(item))
            .ToList();

        var skippedErrorItems = feed.Items.Count() - filteredItems.Count;
        if (skippedErrorItems > 0)
        {
            _logger.LogWarning(
                "Skipped {Count} RSS-Bridge error item(s) from {RssUrl}",
                skippedErrorItems,
                rssUrl);
        }

        var posts = filteredItems
            .Select(MapItem)
            .Where(x => !string.IsNullOrWhiteSpace(x.TweetId))
            .Take(Math.Max(1, maxItems))
            .ToList();

        if (posts.Count == 0 && skippedErrorItems > 0 &&
            trackedFeed.Platform == FeedPlatform.X &&
            trackedFeed.Provider == FeedProvider.RssBridge)
        {
            var fallbackPosts = await TryFetchNitterFallbackAsync(
                rssUrl,
                trackedFeed.SourceKey,
                maxItems,
                cancellationToken);
            if (fallbackPosts.Count > 0)
            {
                return fallbackPosts;
            }
        }

        if (posts.Count == 0 && skippedErrorItems > 0)
        {
            _logger.LogWarning(
                "Feed source {SourceKey} returned only bridge error item(s). Check source visibility/public posts. Url={RssUrl}",
                trackedFeed.SourceKey,
                rssUrl);
        }

        _logger.LogDebug("Fetched {Count} post(s) from {RssUrl}", posts.Count, rssUrl);
        return posts;
    }

    private async Task<IReadOnlyList<RssPost>> TryFetchNitterFallbackAsync(
        string rssUrl,
        string sourceKey,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (!_rssBridgeOptions.CurrentValue.EnableNitterFallback)
        {
            return [];
        }

        if (!TryExtractUsernameFromRssUrl(rssUrl, out var username))
        {
            username = sourceKey;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return [];
        }

        var nitterBaseUrl = _rssBridgeOptions.CurrentValue.NitterBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(nitterBaseUrl))
        {
            return [];
        }

        var fallbackUrl = $"{nitterBaseUrl}/{Uri.EscapeDataString(username)}/rss";

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(fallbackUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Nitter fallback request failed for @{Username}. StatusCode={StatusCode}, Url={Url}",
                    username,
                    (int)response.StatusCode,
                    fallbackUrl);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = false });
            var feed = SyndicationFeed.Load(reader);
            if (feed?.Items is null)
            {
                return [];
            }

            var posts = feed.Items
                .Select(MapItem)
                .Where(x => !string.IsNullOrWhiteSpace(x.TweetId))
                .Take(Math.Max(1, maxItems))
                .ToList();

            if (posts.Count > 0)
            {
                _logger.LogInformation(
                    "Using Nitter fallback for @{Username}. Retrieved {Count} post(s) from {Url}",
                    username,
                    posts.Count,
                    fallbackUrl);
            }

            return posts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Nitter fallback failed for @{Username}. Url={Url}",
                username,
                fallbackUrl);
            return [];
        }
    }

    private static bool TryExtractUsernameFromRssUrl(string rssUrl, out string username)
    {
        username = string.Empty;

        if (!Uri.TryCreate(rssUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!parts[0].Equals("u", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            username = Uri.UnescapeDataString(parts[1]).Trim();
            return !string.IsNullOrWhiteSpace(username);
        }

        return false;
    }

    private static RssPost MapItem(SyndicationItem item)
    {
        var url = item.Links.FirstOrDefault(x => x.RelationshipType == "alternate")?.Uri?.ToString()
            ?? item.Links.FirstOrDefault()?.Uri?.ToString()
            ?? string.Empty;

        var tweetId = string.IsNullOrWhiteSpace(item.Id)
            ? url
            : item.Id;

        if (string.IsNullOrWhiteSpace(tweetId))
        {
            tweetId = $"fallback:{item.Title?.Text}:{item.PublishDate.UtcDateTime:O}";
        }

        var summaryHtml = item.Summary?.Text
            ?? (item.Content as TextSyndicationContent)?.Text
            ?? string.Empty;

        var publishedAtUtc = item.PublishDate != default
            ? item.PublishDate.UtcDateTime
            : DateTime.UtcNow;

        return new RssPost(
            TweetId: tweetId.Trim(),
            Url: url,
            Title: item.Title?.Text ?? string.Empty,
            SummaryHtml: summaryHtml,
            PublishedAtUtc: publishedAtUtc);
    }

    private static bool IsBridgeErrorItem(SyndicationItem item)
    {
        var title = item.Title?.Text ?? string.Empty;
        var summary = item.Summary?.Text
            ?? (item.Content as TextSyndicationContent)?.Text
            ?? string.Empty;

        return title.Contains("Bridge returned error", StringComparison.OrdinalIgnoreCase) ||
               summary.Contains("Bridge returned error", StringComparison.OrdinalIgnoreCase) ||
               summary.Contains("404 Page Not Found", StringComparison.OrdinalIgnoreCase) ||
               summary.Contains("HttpException", StringComparison.OrdinalIgnoreCase);
    }
}
