namespace DiscordXBot.Services.Models;

public sealed record RssPost(
    string TweetId,
    string Url,
    string Title,
    string SummaryHtml,
    DateTime PublishedAtUtc);
