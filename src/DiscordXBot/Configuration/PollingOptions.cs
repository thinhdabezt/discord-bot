namespace DiscordXBot.Configuration;

public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    public int IntervalMinutes { get; set; } = 10;
    public int MaxItemsPerFeed { get; set; } = 5;
}
