namespace DiscordXBot.Services.Models;

public sealed record ParsedTweetContent(
    string Caption,
    IReadOnlyList<string> ImageUrls);
