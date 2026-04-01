using System.Net;
using System.Text.RegularExpressions;
using DiscordXBot.Data.Entities;
using HtmlAgilityPack;
using DiscordXBot.Services.Models;

namespace DiscordXBot.Services;

public sealed class TweetContentParser
{
    public ParsedTweetContent Parse(RssPost post, FeedPlatform platform = FeedPlatform.X)
    {
        var (imageUrls, mediaType) = AnalyzeMedia(post.SummaryHtml, platform);
        var caption = ExtractCaption(post.Title, post.SummaryHtml, post.Url, platform);

        return new ParsedTweetContent(caption, imageUrls, mediaType, post.PublishedAtUtc);
    }

    private static (IReadOnlyList<string> ImageUrls, ParsedMediaType MediaType) AnalyzeMedia(string html, FeedPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return ([], ParsedMediaType.CaptionOnly);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rawImageUrls = doc.DocumentNode
            .SelectNodes("//img[@src]")?
            .Select(x => x.GetAttributeValue("src", string.Empty))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList() ?? [];

        var images = new List<string>();
        var hasGif = false;
        var hasVideoThumbnail = false;

        foreach (var rawUrl in rawImageUrls)
        {
            if (IsGifImageUrl(rawUrl))
            {
                hasGif = true;
                continue;
            }

            if (IsVideoThumbnailUrl(rawUrl))
            {
                hasVideoThumbnail = true;
                continue;
            }

            var normalized = NormalizeImageUrl(rawUrl, platform);
            var sanitized = DiscordUrlSanitizer.Sanitize(normalized);
            if (sanitized is null)
            {
                continue;
            }

            images.Add(sanitized);
        }

        images = DeduplicateImageUrls(images, platform)
            .ToList();

        var hasImage = images.Count > 0;
        var hasVideo = hasVideoThumbnail ||
                       HasVideoTags(doc) ||
                       ContainsVideoIndicators(html);

        var mediaType = ClassifyMediaType(hasImage, hasGif, hasVideo);

        return (images, mediaType);
    }

    private static ParsedMediaType ClassifyMediaType(bool hasImage, bool hasGif, bool hasVideo)
    {
        if (hasImage && (hasGif || hasVideo))
        {
            return ParsedMediaType.Mixed;
        }

        if (hasImage)
        {
            return ParsedMediaType.ImageOnly;
        }

        if (hasGif)
        {
            return ParsedMediaType.GifOnly;
        }

        if (hasVideo)
        {
            return ParsedMediaType.VideoOnly;
        }

        return ParsedMediaType.CaptionOnly;
    }

    private static bool HasVideoTags(HtmlDocument doc)
    {
        return doc.DocumentNode.SelectSingleNode("//video") is not null ||
               doc.DocumentNode.SelectSingleNode("//source") is not null;
    }

    private static bool ContainsVideoIndicators(string html)
    {
        return html.Contains("video.twimg.com", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("ext_tw_video", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("tweet_video", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("amplify_video", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("<video", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGifImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var decoded = Uri.UnescapeDataString(url);
        return decoded.Contains(".gif", StringComparison.OrdinalIgnoreCase) ||
               decoded.Contains("format=gif", StringComparison.OrdinalIgnoreCase) ||
               decoded.Contains("name=gif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoThumbnailUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var decoded = Uri.UnescapeDataString(url);
        return decoded.Contains("tweet_video_thumb/", StringComparison.OrdinalIgnoreCase) ||
               decoded.Contains("amplify_video_thumb/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCaption(string title, string html, string fallbackUrl, FeedPlatform platform)
    {
        var caption = title;

        if (string.IsNullOrWhiteSpace(caption) && !string.IsNullOrWhiteSpace(html))
        {
            caption = ExtractTextFromHtml(html);
        }

        if (platform == FeedPlatform.Facebook)
        {
            caption = CleanFacebookCaption(caption);
        }

        if (string.IsNullOrWhiteSpace(caption))
        {
            caption = fallbackUrl;
        }

        if (caption.Length > 3900)
        {
            caption = caption[..3900] + "...";
        }

        return caption;
    }

    private static string NormalizeImageUrl(string url, FeedPlatform platform)
    {
        var normalized = WebUtility.HtmlDecode(url).Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        normalized = NormalizeNitterImageUrl(normalized);

        if (platform == FeedPlatform.X &&
            normalized.Contains("pbs.twimg.com", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized
                .Replace(":small", ":orig", StringComparison.OrdinalIgnoreCase)
                .Replace(":thumb", ":orig", StringComparison.OrdinalIgnoreCase)
                .Replace(":medium", ":orig", StringComparison.OrdinalIgnoreCase)
                .Replace("name=small", "name=orig", StringComparison.OrdinalIgnoreCase)
                .Replace("name=medium", "name=orig", StringComparison.OrdinalIgnoreCase);
        }

        if (platform == FeedPlatform.Facebook)
        {
            normalized = NormalizeFacebookImageUrl(normalized);
        }

        return normalized;
    }

    private static string ExtractTextFromHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var removableNodes = doc.DocumentNode.SelectNodes("//img|//script|//style");
        if (removableNodes is not null)
        {
            foreach (var node in removableNodes)
            {
                node.Remove();
            }
        }

        return WebUtility.HtmlDecode(doc.DocumentNode.InnerText).Trim();
    }

    private static string CleanFacebookCaption(string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return string.Empty;
        }

        var cleaned = WebUtility.HtmlDecode(caption);
        cleaned = Regex.Replace(cleaned, @"https?://\S+", string.Empty, RegexOptions.IgnoreCase);

        var lines = cleaned
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !IsFacebookNoiseLine(x))
            .Select(DeduplicateHashtagsInLine)
            .ToList();

        return string.Join("\n", lines).Trim();
    }

    private static string DeduplicateHashtagsInLine(string line)
    {
        var seenHashtags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();

        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var hashtag = token.TrimEnd('.', ',', ';', ':', '!', '?');
            if (hashtag.StartsWith('#') && hashtag.Length > 1)
            {
                var hashtagKey = hashtag[1..];
                if (!seenHashtags.Add(hashtagKey))
                {
                    continue;
                }
            }

            output.Add(token);
        }

        return string.Join(" ", output);
    }

    private static bool IsFacebookNoiseLine(string line)
    {
        return line.Equals("See more", StringComparison.OrdinalIgnoreCase) ||
               line.Equals("View on Facebook", StringComparison.OrdinalIgnoreCase) ||
               line.Equals("View post on Facebook", StringComparison.OrdinalIgnoreCase) ||
               line.Equals("Like Comment Share", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("All reactions", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(line, @"^\d+\s+(likes?|comments?|shares?)$", RegexOptions.IgnoreCase);
    }

    private static string NormalizeFacebookImageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (uri.AbsolutePath.Contains("safe_image.php", StringComparison.OrdinalIgnoreCase) &&
            TryReadQueryValue(uri.Query, "url", out var safeImageTarget) &&
            Uri.TryCreate(safeImageTarget, UriKind.Absolute, out _))
        {
            return safeImageTarget;
        }

        if ((uri.Host.Equals("l.facebook.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("lm.facebook.com", StringComparison.OrdinalIgnoreCase)) &&
            TryReadQueryValue(uri.Query, "u", out var externalTarget) &&
            Uri.TryCreate(externalTarget, UriKind.Absolute, out _))
        {
            return externalTarget;
        }

        return url;
    }

    private static bool TryReadQueryValue(string query, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (!pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = Uri.UnescapeDataString(pair[1]);
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static IReadOnlyList<string> DeduplicateImageUrls(IEnumerable<string> images, FeedPlatform platform)
    {
        var output = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in images)
        {
            var dedupeKey = BuildImageDedupeKey(image, platform);
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            output.Add(image);
        }

        return output;
    }

    private static string BuildImageDedupeKey(string imageUrl, FeedPlatform platform)
    {
        if (platform != FeedPlatform.Facebook)
        {
            return imageUrl;
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return imageUrl;
        }

        if (uri.Host.Contains("fbcdn.net", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("scontent", StringComparison.OrdinalIgnoreCase))
        {
            return $"{uri.Host.ToLowerInvariant()}{uri.AbsolutePath.ToLowerInvariant()}";
        }

        var filteredQuery = string.Join(
            "&",
            uri.Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !x.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) &&
                            !x.StartsWith("fbclid=", StringComparison.OrdinalIgnoreCase) &&
                            !x.StartsWith("refsrc=", StringComparison.OrdinalIgnoreCase) &&
                            !x.StartsWith("__tn__=", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(filteredQuery)
            ? $"{uri.Host.ToLowerInvariant()}{uri.AbsolutePath.ToLowerInvariant()}"
            : $"{uri.Host.ToLowerInvariant()}{uri.AbsolutePath.ToLowerInvariant()}?{filteredQuery}";
    }

    private static string NormalizeNitterImageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (!uri.Host.Contains("nitter", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var marker = "/pic/";
        var markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return url;
        }

        var encodedPayload = uri.AbsolutePath[(markerIndex + marker.Length)..];
        if (string.IsNullOrWhiteSpace(encodedPayload))
        {
            return url;
        }

        var decoded = Uri.UnescapeDataString(encodedPayload);
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return url;
        }

        if (Uri.TryCreate(decoded, UriKind.Absolute, out var absoluteDecoded))
        {
            return absoluteDecoded.ToString();
        }

        if (decoded.StartsWith("media/", StringComparison.OrdinalIgnoreCase) ||
            decoded.StartsWith("profile_images/", StringComparison.OrdinalIgnoreCase) ||
            decoded.StartsWith("amplify_video_thumb/", StringComparison.OrdinalIgnoreCase) ||
            decoded.StartsWith("tweet_video_thumb/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://pbs.twimg.com/" + decoded;
        }

        return url;
    }
}
