using DiscordXBot.Configuration;
using DiscordXBot.Data.Entities;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Services;

public sealed class FeedUrlResolver(
    IOptionsMonitor<RssBridgeOptions> rssBridgeOptions,
    IOptionsMonitor<FeedProviderOptions> feedProviderOptions)
{
    private readonly IOptionsMonitor<RssBridgeOptions> _rssBridgeOptions = rssBridgeOptions;
    private readonly IOptionsMonitor<FeedProviderOptions> _feedProviderOptions = feedProviderOptions;

    public FeedProvider GetDefaultProvider(FeedPlatform platform)
    {
        var options = _feedProviderOptions.CurrentValue;
        return platform switch
        {
            FeedPlatform.Facebook => FeedProvider.Apify,
            FeedPlatform.Instagram => FeedProvider.RssBridge,
            _ => options.DefaultXProvider
        };
    }

    public bool IsProviderEnabled(FeedProvider provider)
    {
        var options = _feedProviderOptions.CurrentValue;

        return provider switch
        {
            FeedProvider.RssBridge => true,
            FeedProvider.DirectRss => options.EnableDirectRss,
            FeedProvider.Apify => true,
#pragma warning disable CS0618
            FeedProvider.RssHub => false,
#pragma warning restore CS0618
            _ => false
        };
    }

    public string Resolve(
        FeedPlatform platform,
        FeedProvider provider,
        string source,
        FacebookSourceType facebookSourceType = FacebookSourceType.Fanpage)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Feed source cannot be empty.", nameof(source));
        }

        var value = source.Trim();

        return provider switch
        {
            FeedProvider.RssBridge => ResolveRssBridge(platform, value, facebookSourceType),
            FeedProvider.DirectRss => ResolveDirectRss(value),
            FeedProvider.Apify => ResolveApify(platform, value),
            _ => throw new NotSupportedException($"Unsupported provider: {provider}")
        };
    }

    public string ResolveEffectiveFeedUrl(TrackedFeed trackedFeed)
    {
        if ((trackedFeed.Platform == FeedPlatform.X || trackedFeed.Platform == FeedPlatform.Instagram) &&
            trackedFeed.Provider == FeedProvider.RssBridge)
        {
            var source = string.IsNullOrWhiteSpace(trackedFeed.SourceKey)
                ? trackedFeed.XUsername
                : trackedFeed.SourceKey;

            return Resolve(
                trackedFeed.Platform,
                FeedProvider.RssBridge,
                source,
                trackedFeed.SourceType);
        }

        return trackedFeed.RssUrl;
    }

    private string ResolveRssBridge(FeedPlatform platform, string source, FacebookSourceType facebookSourceType)
    {
        var baseUrl = _rssBridgeOptions.CurrentValue.BaseUrl.TrimEnd('/');

        return platform switch
        {
            FeedPlatform.X => $"{baseUrl}/?action=display&bridge=TwitterBridge&context=By+username&u={Uri.EscapeDataString(source)}&format=Atom",
            FeedPlatform.Instagram => $"{baseUrl}/?action=display&bridge=InstagramBridge&context=Username&u={Uri.EscapeDataString(source)}&media_type=all&direct_links=on&format=Atom",
            _ => throw new NotSupportedException($"Unsupported platform: {platform}")
        };
    }

    private static string ResolveApify(FeedPlatform platform, string source)
    {
        if (platform != FeedPlatform.Facebook)
        {
            throw new NotSupportedException("Apify provider is only supported for Facebook feeds.");
        }

        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        return $"https://www.facebook.com/{Uri.EscapeDataString(source)}/";
    }

    private static string ResolveDirectRss(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Direct RSS source must be an absolute URL.");
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Direct RSS source must use http/https.");
        }

        return uri.ToString();
    }

}
