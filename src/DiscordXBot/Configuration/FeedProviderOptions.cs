using DiscordXBot.Data.Entities;

namespace DiscordXBot.Configuration;

public sealed class FeedProviderOptions
{
    public const string SectionName = "FeedProviders";

    public bool EnableDirectRss { get; set; } = true;
    public FeedProvider DefaultXProvider { get; set; } = FeedProvider.RssBridge;
}
