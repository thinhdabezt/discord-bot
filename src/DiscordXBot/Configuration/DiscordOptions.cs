namespace DiscordXBot.Configuration;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string Token { get; set; } = string.Empty;
    public ulong? DevGuildId { get; set; }
}
