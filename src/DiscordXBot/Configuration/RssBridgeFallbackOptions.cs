namespace DiscordXBot.Configuration;

public sealed class RssBridgeFallbackOptions
{
    public const string SectionName = "RssBridgeFallback";

    public bool Enabled { get; set; } = false;
    public int FailureThreshold { get; set; } = 2;
    public int CooldownMinutes { get; set; } = 60;
    public bool EnableForFanpage { get; set; } = true;
    public bool EnableForProfile { get; set; } = false;
}