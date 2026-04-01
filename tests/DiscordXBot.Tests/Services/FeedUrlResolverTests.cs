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
    public void Resolve_UsesRssHubForFacebook()
    {
        var resolver = CreateResolver(
            new RssBridgeOptions(),
            new FeedProviderOptions { RssHubBaseUrl = "http://rsshub:1200" });

        var url = resolver.Resolve(FeedPlatform.Facebook, FeedProvider.RssHub, "nvidia");

        Assert.Equal("http://rsshub:1200/facebook/page/nvidia", url);
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
            new FeedProviderOptions
            {
                EnableDirectRss = false,
                EnableRssHub = true
            });

        Assert.False(resolver.IsProviderEnabled(FeedProvider.DirectRss));
        Assert.True(resolver.IsProviderEnabled(FeedProvider.RssHub));
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
