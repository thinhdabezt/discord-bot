using System.ServiceModel.Syndication;
using System.Xml;
using DiscordXBot.Services.Models;

namespace DiscordXBot.Services;

public sealed class RssBridgeClient(IHttpClientFactory httpClientFactory, ILogger<RssBridgeClient> logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<RssBridgeClient> _logger = logger;

    public async Task<IReadOnlyList<RssPost>> GetPostsAsync(string rssUrl, int maxItems, CancellationToken cancellationToken)
    {
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

        _logger.LogDebug("Fetched {Count} post(s) from {RssUrl}", posts.Count, rssUrl);
        return posts;
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
