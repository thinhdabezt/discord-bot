namespace DiscordXBot.Configuration;

public sealed class ApifyFallbackOptions
{
    public const string SectionName = "ApifyFallback";

    public bool Enabled { get; set; } = false;
    public string ApiBaseUrl { get; set; } = "https://api.apify.com/v2";
    public string ApiToken { get; set; } = string.Empty;
    public string ActorId { get; set; } = "apify/facebook-posts-scraper";
    public int ResultsLimit { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 45;
    public int PollIntervalSeconds { get; set; } = 5;
    public int MaxPollAttempts { get; set; } = 24;
    public int FailureThreshold { get; set; } = 3;
    public int CooldownMinutes { get; set; } = 180;
    public bool EnableForFanpage { get; set; } = true;
    public bool EnableForProfile { get; set; } = true;
}
