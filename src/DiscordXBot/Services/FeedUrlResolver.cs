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
            FeedPlatform.Facebook => options.DefaultFacebookProvider,
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
            FeedProvider.RssHub => options.EnableRssHub,
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
            FeedProvider.RssHub => ResolveRssHub(platform, value, facebookSourceType),
            _ => throw new NotSupportedException($"Unsupported provider: {provider}")
        };
    }

    private string ResolveRssBridge(FeedPlatform platform, string source, FacebookSourceType facebookSourceType)
    {
        var baseUrl = _rssBridgeOptions.CurrentValue.BaseUrl.TrimEnd('/');

        return platform switch
        {
            FeedPlatform.X => $"{baseUrl}/?action=display&bridge=TwitterBridge&context=By+username&u={Uri.EscapeDataString(source)}&format=Atom",
            FeedPlatform.Facebook when facebookSourceType == FacebookSourceType.Fanpage =>
                $"{baseUrl}/?action=display&bridge=FacebookBridge&context=User&u={Uri.EscapeDataString(source)}&media_type=all&format=Atom",
            FeedPlatform.Facebook => throw new NotSupportedException("Facebook profile is not supported by RSS-Bridge provider."),
            _ => throw new NotSupportedException($"Unsupported platform: {platform}")
        };
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

    private string ResolveRssHub(FeedPlatform platform, string source, FacebookSourceType facebookSourceType)
    {
        return platform switch
        {
            FeedPlatform.X => ResolveXViaRssHub(source),
            FeedPlatform.Facebook => ResolveFacebookViaRssHub(source, facebookSourceType),
            _ => throw new NotSupportedException($"Unsupported platform: {platform}")
        };
    }

    private string ResolveXViaRssHub(string source)
    {
        var baseUrl = _feedProviderOptions.CurrentValue.RssHubBaseUrl.TrimEnd('/');
        return $"{baseUrl}/twitter/user/{Uri.EscapeDataString(source)}";
    }

    private string ResolveFacebookViaRssHub(string source, FacebookSourceType sourceType)
    {
        var baseUrl = _feedProviderOptions.CurrentValue.RssHubBaseUrl.TrimEnd('/');

        return sourceType switch
        {
            FacebookSourceType.Fanpage => $"{baseUrl}/facebook/page/{Uri.EscapeDataString(source)}",
            FacebookSourceType.Profile => $"{baseUrl}/facebook/user/{Uri.EscapeDataString(source)}",
            _ => throw new NotSupportedException($"Unsupported Facebook source type: {sourceType}")
        };
    }
}
