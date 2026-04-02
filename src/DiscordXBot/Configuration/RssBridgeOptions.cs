namespace DiscordXBot.Configuration;

public sealed class RssBridgeOptions
{
    public const string SectionName = "RssBridge";

    public string BaseUrl { get; set; } = string.Empty;
    public bool EnableNitterFallback { get; set; } = true;
    public string NitterBaseUrl { get; set; } = "https://nitter.net";
}
