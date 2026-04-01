using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DiscordXBot.Configuration;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services.Models;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Services;

public sealed class ApifyFacebookClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ApifyFallbackOptions> apifyOptions,
    ILogger<ApifyFacebookClient> logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptionsMonitor<ApifyFallbackOptions> _apifyOptions = apifyOptions;
    private readonly ILogger<ApifyFacebookClient> _logger = logger;

    public async Task<IReadOnlyList<RssPost>> FetchPostsAsync(
        TrackedFeed trackedFeed,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var options = _apifyOptions.CurrentValue;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiToken))
        {
            return [];
        }

        if (!IsSourceTypeEnabled(trackedFeed.SourceType, options))
        {
            return [];
        }

        if (trackedFeed.Platform != FeedPlatform.Facebook)
        {
            return [];
        }

        var inputUrl = BuildFacebookInputUrl(trackedFeed);
        if (string.IsNullOrWhiteSpace(inputUrl))
        {
            return [];
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, options.RequestTimeoutSeconds)));

        var actorPath = NormalizeActorPath(options.ActorId);
        var runUrl = BuildRunUrl(options.ApiBaseUrl, actorPath, options.ApiToken);

        try
        {
            var limit = Math.Max(1, Math.Min(maxItems, options.ResultsLimit));
            var runId = await StartRunAsync(runUrl, inputUrl, limit, timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(runId))
            {
                return [];
            }

            var datasetId = await WaitForRunAndGetDatasetIdAsync(
                options.ApiBaseUrl,
                runId,
                options.ApiToken,
                timeoutCts.Token);

            if (string.IsNullOrWhiteSpace(datasetId))
            {
                _logger.LogWarning(
                    "Apify fallback run finished without dataset id. Source={SourceKey}, RunId={RunId}",
                    trackedFeed.SourceKey,
                    runId);
                return [];
            }

            var posts = await FetchDatasetPostsAsync(
                options.ApiBaseUrl,
                datasetId,
                options.ApiToken,
                limit,
                timeoutCts.Token);

            if (posts.Count > 0)
            {
                _logger.LogInformation(
                    "Apify fallback retrieved {Count} post(s) for source {SourceKey}. RunId={RunId}",
                    posts.Count,
                    trackedFeed.SourceKey,
                    runId);
            }

            return posts;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Apify fallback timed out for source {SourceKey}",
                trackedFeed.SourceKey);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Apify fallback failed for source {SourceKey}",
                trackedFeed.SourceKey);
            return [];
        }
    }

    private async Task<string?> StartRunAsync(
        string runUrl,
        string inputUrl,
        int limit,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            startUrls = new[] { new { url = inputUrl } },
            resultsLimit = limit
        };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(runUrl, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Apify run start failed. StatusCode={StatusCode}, Url={RunUrl}",
                (int)response.StatusCode,
                runUrl);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return TryGetNestedString(doc.RootElement, "data", "id");
    }

    private async Task<string?> WaitForRunAndGetDatasetIdAsync(
        string apiBaseUrl,
        string runId,
        string token,
        CancellationToken cancellationToken)
    {
        var options = _apifyOptions.CurrentValue;
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));
        var maxPollAttempts = Math.Max(1, options.MaxPollAttempts);
        var runUrl = BuildRunStatusUrl(apiBaseUrl, runId, token);

        for (var attempt = 0; attempt < maxPollAttempts; attempt++)
        {
            var statusDoc = await GetJsonDocumentAsync(runUrl, cancellationToken);
            if (statusDoc is null)
            {
                await Task.Delay(pollInterval, cancellationToken);
                continue;
            }

            using (statusDoc)
            {
                var status = TryGetNestedString(statusDoc.RootElement, "data", "status")?.ToUpperInvariant();
                var datasetId = TryGetNestedString(statusDoc.RootElement, "data", "defaultDatasetId");

                if (status is "SUCCEEDED")
                {
                    return datasetId;
                }

                if (status is "FAILED" or "ABORTED" or "TIMED-OUT")
                {
                    _logger.LogWarning(
                        "Apify run ended with status {Status}. RunId={RunId}",
                        status,
                        runId);
                    return null;
                }
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<RssPost>> FetchDatasetPostsAsync(
        string apiBaseUrl,
        string datasetId,
        string token,
        int limit,
        CancellationToken cancellationToken)
    {
        var itemsUrl = BuildDatasetItemsUrl(apiBaseUrl, datasetId, token, limit);
        var doc = await GetJsonDocumentAsync(itemsUrl, cancellationToken);
        if (doc is null)
        {
            return [];
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var posts = new List<RssPost>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var post = MapItemToPost(item);
                if (post is null)
                {
                    continue;
                }

                posts.Add(post);
                if (posts.Count >= limit)
                {
                    break;
                }
            }

            return posts;
        }
    }

    private async Task<JsonDocument?> GetJsonDocumentAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Apify request failed. StatusCode={StatusCode}, Url={Url}",
                (int)response.StatusCode,
                url);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static RssPost? MapItemToPost(JsonElement item)
    {
        var postUrl = GetString(item, "topLevelUrl")
            ?? GetString(item, "url")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(postUrl) || !Uri.TryCreate(postUrl, UriKind.Absolute, out _))
        {
            return null;
        }

        var postId = GetString(item, "postId")
            ?? GetString(item, "id")
            ?? postUrl;

        var text = GetString(item, "text")
            ?? GetString(item, "postText")
            ?? GetString(item, "caption")
            ?? string.Empty;

        var publishedAtUtc = ResolvePublishedAtUtc(item);
        var imageUrls = ExtractImageUrls(item);

        var title = string.IsNullOrWhiteSpace(text)
            ? postUrl
            : (text.Length > 280 ? text[..280] : text);

        var summaryHtml = BuildSummaryHtml(text, imageUrls);

        return new RssPost(
            TweetId: postId.Trim(),
            Url: postUrl,
            Title: title,
            SummaryHtml: summaryHtml,
            PublishedAtUtc: publishedAtUtc);
    }

    private static DateTime ResolvePublishedAtUtc(JsonElement item)
    {
        var timeValue = GetString(item, "time");
        if (!string.IsNullOrWhiteSpace(timeValue) && DateTimeOffset.TryParse(timeValue, out var parsed))
        {
            return parsed.UtcDateTime;
        }

        if (TryGetPropertyIgnoreCase(item, "timestamp", out var tsElement))
        {
            if (tsElement.ValueKind == JsonValueKind.Number && tsElement.TryGetInt64(out var tsSeconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(tsSeconds).UtcDateTime;
            }

            if (tsElement.ValueKind == JsonValueKind.String && long.TryParse(tsElement.GetString(), out var tsFromString))
            {
                return DateTimeOffset.FromUnixTimeSeconds(tsFromString).UtcDateTime;
            }
        }

        return DateTime.UtcNow;
    }

    private static List<string> ExtractImageUrls(JsonElement item)
    {
        var urls = new List<string>();

        if (!TryGetPropertyIgnoreCase(item, "media", out var mediaElement) || mediaElement.ValueKind != JsonValueKind.Array)
        {
            return urls;
        }

        foreach (var media in mediaElement.EnumerateArray())
        {
            AddCandidate(urls, GetString(media, "thumbnail"));
            AddCandidate(urls, GetString(media, "url"));
            AddCandidate(urls, GetString(media, "src"));

            if (TryGetPropertyIgnoreCase(media, "photo_image", out var photoImageElement) &&
                photoImageElement.ValueKind == JsonValueKind.Object)
            {
                AddCandidate(urls, GetString(photoImageElement, "uri"));
                AddCandidate(urls, GetString(photoImageElement, "url"));
            }

            if (TryGetPropertyIgnoreCase(media, "image", out var imageElement))
            {
                if (imageElement.ValueKind == JsonValueKind.String)
                {
                    AddCandidate(urls, imageElement.GetString());
                }
                else if (imageElement.ValueKind == JsonValueKind.Object)
                {
                    AddCandidate(urls, GetString(imageElement, "uri"));
                    AddCandidate(urls, GetString(imageElement, "url"));
                }
            }
        }

        return urls;
    }

    private static void AddCandidate(List<string> urls, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (urls.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        urls.Add(candidate);
    }

    private static string BuildSummaryHtml(string text, IReadOnlyList<string> imageUrls)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(text))
        {
            sb.Append("<p>");
            sb.Append(WebUtility.HtmlEncode(text));
            sb.Append("</p>");
        }

        foreach (var image in imageUrls)
        {
            sb.Append("<img src='");
            sb.Append(WebUtility.HtmlEncode(image));
            sb.Append("' />");
        }

        return sb.ToString();
    }

    private static bool IsSourceTypeEnabled(FacebookSourceType sourceType, ApifyFallbackOptions options)
    {
        return sourceType switch
        {
            FacebookSourceType.Fanpage => options.EnableForFanpage,
            FacebookSourceType.Profile => options.EnableForProfile,
            _ => false
        };
    }

    private static string BuildFacebookInputUrl(TrackedFeed trackedFeed)
    {
        var source = trackedFeed.SourceKey?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        return $"https://www.facebook.com/{Uri.EscapeDataString(source)}/";
    }

    private static string NormalizeActorPath(string actorId)
    {
        var normalized = actorId.Trim();
        if (normalized.Contains('/'))
        {
            normalized = normalized.Replace('/', '~');
        }

        return normalized;
    }

    private static string BuildRunUrl(string apiBaseUrl, string actorPath, string token)
    {
        var baseUrl = apiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/acts/{actorPath}/runs?token={Uri.EscapeDataString(token)}";
    }

    private static string BuildRunStatusUrl(string apiBaseUrl, string runId, string token)
    {
        var baseUrl = apiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/actor-runs/{Uri.EscapeDataString(runId)}?token={Uri.EscapeDataString(token)}";
    }

    private static string BuildDatasetItemsUrl(string apiBaseUrl, string datasetId, string token, int limit)
    {
        var baseUrl = apiBaseUrl.TrimEnd('/');
        var boundedLimit = Math.Max(1, limit);
        return $"{baseUrl}/datasets/{Uri.EscapeDataString(datasetId)}/items?token={Uri.EscapeDataString(token)}&format=json&clean=true&desc=true&limit={boundedLimit}";
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var valueElement))
        {
            return null;
        }

        return valueElement.ValueKind switch
        {
            JsonValueKind.String => valueElement.GetString(),
            JsonValueKind.Number => valueElement.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static string? TryGetNestedString(JsonElement root, string parentProperty, string childProperty)
    {
        if (!TryGetPropertyIgnoreCase(root, parentProperty, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parent, childProperty);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
