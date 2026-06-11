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
    public void Resolve_ThrowsForFacebookRssBridge()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        Assert.Throws<NotSupportedException>(() =>
            resolver.Resolve(FeedPlatform.Facebook, FeedProvider.RssBridge, "10150123547145211"));
    }

    [Fact]
    public void Resolve_UsesStableFacebookUrlForApify()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var url = resolver.Resolve(
            FeedPlatform.Facebook,
            FeedProvider.Apify,
            "nasa",
            FacebookSourceType.Profile);

        Assert.Equal("https://www.facebook.com/nasa/", url);
    }

    [Fact]
    public void GetDefaultProvider_UsesApifyForFacebook()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var provider = resolver.GetDefaultProvider(FeedPlatform.Facebook);

        Assert.Equal(FeedProvider.Apify, provider);
    }

#pragma warning disable CS0618
    [Fact]
    public void IsProviderEnabled_DisablesLegacyRssHub()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        Assert.False(resolver.IsProviderEnabled(FeedProvider.RssHub));
    }
#pragma warning restore CS0618

    [Fact]
    public void ResolveEffectiveFeedUrl_KeepsApifyFacebookUrl()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions { BaseUrl = "http://rss-bridge:80" },
            new FeedProviderOptions());

        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.Facebook,
            Provider = FeedProvider.Apify,
            SourceType = FacebookSourceType.Profile,
            SourceKey = "61574718883158",
            XUsername = "fb_61574718883158",
            RssUrl = "https://www.facebook.com/61574718883158/"
        };

        var url = resolver.ResolveEffectiveFeedUrl(feed);

        Assert.Equal("https://www.facebook.com/61574718883158/", url);
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
        Assert.True(resolver.IsProviderEnabled(FeedProvider.Apify));
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
