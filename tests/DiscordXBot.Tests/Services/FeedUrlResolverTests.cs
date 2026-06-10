using DiscordXBot.Configuration;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Tests.Services;

public class FeedUrlResolverTests
{
    [Fact]
    public void Resolve_UsesRssBridgeForX()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var url = resolver.Resolve(FeedPlatform.X, FeedProvider.RssBridge, "medrives1338");

        Assert.Equal(
            "http://rss-bridge:80/?action=display&bridge=TwitterBridge&context=By+username&u=medrives1338&format=Atom",
            url);
    }

    [Fact]
    public void Resolve_UsesRssBridgeForFacebook()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var url = resolver.Resolve(FeedPlatform.Facebook, FeedProvider.RssBridge, "10150123547145211");

        Assert.Equal(
            "http://rss-bridge:80/?action=display&bridge=FacebookBridge&context=User&u=10150123547145211&media_type=all&format=Atom",
            url);
    }

    [Fact]
    public void Resolve_UsesRssBridgeForFacebookProfile()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var url = resolver.Resolve(
            FeedPlatform.Facebook,
            FeedProvider.RssBridge,
            "10001234567890",
            FacebookSourceType.Profile);

        Assert.Equal(
            "http://rss-bridge:80/?action=display&bridge=FacebookBridge&context=User&u=10001234567890&media_type=all&format=Atom",
            url);
    }

    [Fact]
    public void GetDefaultProvider_UsesRssBridgeForFacebook()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var provider = resolver.GetDefaultProvider(FeedPlatform.Facebook);

        Assert.Equal(FeedProvider.RssBridge, provider);
    }

#pragma warning disable CS0618
    [Fact]
    public void GetDefaultProvider_IgnoresLegacyRssHubFacebookConfig()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions { DefaultFacebookProvider = FeedProvider.RssHub });

        var provider = resolver.GetDefaultProvider(FeedPlatform.Facebook);

        Assert.Equal(FeedProvider.RssBridge, provider);
    }
#pragma warning restore CS0618

    [Fact]
    public void ResolveEffectiveFeedUrl_RefreshesFacebookRssBridgeUrlFromSourceKey()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.Facebook,
            Provider = FeedProvider.RssBridge,
            SourceType = FacebookSourceType.Profile,
            SourceKey = "61574718883158",
            XUsername = "fb_61574718883158",
            RssUrl = "http://rsshub:1200/facebook/user/61574718883158"
        };

        var url = resolver.ResolveEffectiveFeedUrl(feed);

        Assert.Equal(
            "http://rss-bridge:80/?action=display&bridge=FacebookBridge&context=User&u=61574718883158&media_type=all&format=Atom",
            url);
    }

    [Fact]
    public void ResolveEffectiveFeedUrl_KeepsDirectRssUrl()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.Facebook,
            Provider = FeedProvider.DirectRss,
            SourceType = FacebookSourceType.Fanpage,
            SourceKey = "https://example.com/feed.xml",
            RssUrl = "https://example.com/feed.xml"
        };

        var url = resolver.ResolveEffectiveFeedUrl(feed);

        Assert.Equal("https://example.com/feed.xml", url);
    }

    [Fact]
    public void Resolve_ThrowsForDirectRssWithoutAbsoluteUrl()
    {
        var resolver = CreateResolver(new RssBridgeOptions(), new FeedProviderOptions());

        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(FeedPlatform.X, FeedProvider.DirectRss, "not-a-url"));
    }

    [Fact]
    public void IsProviderEnabled_RespectsFeatureToggles()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions(),
            new FeedProviderOptions { EnableDirectRss = false });

        Assert.False(resolver.IsProviderEnabled(FeedProvider.DirectRss));
        Assert.True(resolver.IsProviderEnabled(FeedProvider.RssBridge));
    }

    private static FeedUrlResolver CreateResolver(RssBridgeOptions rssBridge, FeedProviderOptions provider)
    {
        return new FeedUrlResolver(
            new TestOptionsMonitor<RssBridgeOptions>(rssBridge),
            new TestOptionsMonitor<FeedProviderOptions>(provider));
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
