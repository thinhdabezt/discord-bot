namespace DiscordXBot.Configuration;

public sealed class PublishOptions
{
    public const string SectionName = "Publish";

    public int MaxConcurrentPublishes { get; set; } = 2;
    public int InterPublishDelayMs { get; set; } = 200;
    public int MaxAdditionalImages { get; set; } = 3;
}
