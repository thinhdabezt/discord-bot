namespace DiscordXBot.Data.Entities;

public enum FeedPlatform
{
    X = 0,
    Facebook = 1
}

public enum FacebookSourceType
{
    Fanpage = 0,
    Profile = 1
}

public enum FeedProvider
{
    RssBridge = 0,
    DirectRss = 1,

    [Obsolete("RSSHub has been removed. Legacy value is retained only for config/database migration compatibility.")]
    RssHub = 2,

    Apify = 3
}
