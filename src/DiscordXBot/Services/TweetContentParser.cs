using System.Net;
using HtmlAgilityPack;
using DiscordXBot.Services.Models;

namespace DiscordXBot.Services;

public sealed class TweetContentParser
{
    public ParsedTweetContent Parse(RssPost post)
    {
        var imageUrls = ExtractImages(post.SummaryHtml);
        var caption = ExtractCaption(post.Title, post.SummaryHtml, post.Url);

        return new ParsedTweetContent(caption, imageUrls);
    }

    private static IReadOnlyList<string> ExtractImages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var images = doc.DocumentNode
            .SelectNodes("//img[@src]")?
            .Select(x => x.GetAttributeValue("src", string.Empty))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeImageUrl)
            .Select(DiscordUrlSanitizer.Sanitize)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        return images;
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
