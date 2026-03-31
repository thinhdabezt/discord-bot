namespace DiscordXBot.Configuration;

public sealed class RetryOptions
{
    public const string SectionName = "Retry";

    public int MaxRetries { get; set; } = 3;
    public int PublishMaxRetries { get; set; } = 2;
    public int InitialDelaySeconds { get; set; } = 2;
}
