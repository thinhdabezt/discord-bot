namespace DiscordXBot.Services.Models;

public enum ParsedMediaType
{
    CaptionOnly = 0,
    ImageOnly = 1,
    GifOnly = 2,
    VideoOnly = 3,
    Mixed = 4
}

public sealed record ParsedTweetContent(
    string Caption,
    IReadOnlyList<string> ImageUrls,
    ParsedMediaType MediaType,
    DateTime PostedAtUtc);
