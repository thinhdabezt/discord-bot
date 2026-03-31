using System.Net;
using HtmlAgilityPack;
using DiscordXBot.Services.Models;

namespace DiscordXBot.Services;

public sealed class TweetContentParser
{
    public ParsedTweetContent Parse(RssPost post)
    {
        var (imageUrls, mediaType) = AnalyzeMedia(post.SummaryHtml);
        var caption = ExtractCaption(post.Title, post.SummaryHtml, post.Url);

        return new ParsedTweetContent(caption, imageUrls, mediaType, post.PublishedAtUtc);
    }

    private static (IReadOnlyList<string> ImageUrls, ParsedMediaType MediaType) AnalyzeMedia(string html)
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

            var normalized = NormalizeImageUrl(rawUrl);
            var sanitized = DiscordUrlSanitizer.Sanitize(normalized);
            if (sanitized is null)
            {
                continue;
            }

            images.Add(sanitized);
        }

        images = images
            .Distinct(StringComparer.OrdinalIgnoreCase)
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

    private static string ExtractCaption(string title, string html, string fallbackUrl)
    {
        var caption = title;

        if (string.IsNullOrWhiteSpace(caption) && !string.IsNullOrWhiteSpace(html))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var imageNodes = doc.DocumentNode.SelectNodes("//img");
            if (imageNodes is not null)
            {
                foreach (var node in imageNodes)
                {
                    node.Remove();
                }
            }

            caption = WebUtility.HtmlDecode(doc.DocumentNode.InnerText).Trim();
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

    private static string NormalizeImageUrl(string url)
    {
        var normalized = WebUtility.HtmlDecode(url).Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        normalized = NormalizeNitterImageUrl(normalized);

        if (normalized.Contains("pbs.twimg.com", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized
                .Replace(":small", ":orig", StringComparison.OrdinalIgnoreCase)
                .Replace(":thumb", ":orig", StringComparison.OrdinalIgnoreCase)
                .Replace(":medium", ":orig", StringComparison.OrdinalIgnoreCase)
                .Replace("name=small", "name=orig", StringComparison.OrdinalIgnoreCase)
                .Replace("name=medium", "name=orig", StringComparison.OrdinalIgnoreCase);
        }

        return normalized;
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
