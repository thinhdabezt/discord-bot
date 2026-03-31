namespace DiscordXBot.Data.Entities;

public sealed class ProcessedTweet
{
    public long Id { get; set; }
    public long TrackedFeedId { get; set; }
    public long GuildId { get; set; }
    public long ChannelId { get; set; }
    public string XUsername { get; set; } = string.Empty;
    public string TweetId { get; set; } = string.Empty;
    public string? TweetUrl { get; set; }
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;

    public TrackedFeed TrackedFeed { get; set; } = null!;
}
