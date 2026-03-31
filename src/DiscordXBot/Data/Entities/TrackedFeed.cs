namespace DiscordXBot.Data.Entities;

public sealed class TrackedFeed
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public long ChannelId { get; set; }
    public string XUsername { get; set; } = string.Empty;
    public string RssUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ProcessedTweet> ProcessedTweets { get; set; } = new List<ProcessedTweet>();
}
