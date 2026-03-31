namespace DiscordXBot.Configuration;

public sealed class RssBridgeOptions
{
    public const string SectionName = "RssBridge";

    public string BaseUrl { get; set; } = "http://localhost:3000";
}
