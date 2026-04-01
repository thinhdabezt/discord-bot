using DiscordXBot.Data.Entities;

namespace DiscordXBot.Configuration;

public sealed class FeedProviderOptions
{
    public const string SectionName = "FeedProviders";

    public bool EnableDirectRss { get; set; } = true;
    public bool EnableRssHub { get; set; } = false;
    public string RssHubBaseUrl { get; set; } = "http://rsshub:1200";
    public FeedProvider DefaultXProvider { get; set; } = FeedProvider.RssBridge;
    public FeedProvider DefaultFacebookProvider { get; set; } = FeedProvider.RssHub;
    public bool EnableFacebookProfileAlerts { get; set; } = false;
    public ulong FacebookProfileAlertChannelId { get; set; }
    public int FacebookProfileFailureThreshold { get; set; } = 3;
    public int FacebookProfileAlertCooldownMinutes { get; set; } = 180;
}
