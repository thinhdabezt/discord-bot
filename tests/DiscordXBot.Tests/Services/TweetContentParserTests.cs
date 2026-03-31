using DiscordXBot.Services;
using DiscordXBot.Services.Models;

namespace DiscordXBot.Tests.Services;

public class TweetContentParserTests
{
    private readonly TweetContentParser _parser = new();

    [Fact]
    public void Parse_ExtractsAndNormalizesMultipleImages()
    {
        var post = new RssPost(
            TweetId: "tweet-1",
            Url: "https://x.com/u/status/1",
            Title: "Caption",
            SummaryHtml: "<p>hello</p><img src='https://pbs.twimg.com/media/a.jpg:small'/><img src='https://pbs.twimg.com/media/b.jpg?name=small'/>",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Equal("Caption", result.Caption);
        Assert.Equal(2, result.ImageUrls.Count);
        Assert.Contains("https://pbs.twimg.com/media/a.jpg:orig", result.ImageUrls);
        Assert.Contains("https://pbs.twimg.com/media/b.jpg?name=orig", result.ImageUrls);
    }

    [Fact]
    public void Parse_UsesFallbackUrlWhenCaptionIsEmpty()
    {
        var post = new RssPost(
            TweetId: "tweet-2",
            Url: "https://x.com/u/status/2",
            Title: string.Empty,
            SummaryHtml: string.Empty,
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Equal("https://x.com/u/status/2", result.Caption);
        Assert.Empty(result.ImageUrls);
    }

    [Fact]
    public void Parse_TruncatesLongCaption()
    {
        var longTitle = new string('x', 4200);
        var post = new RssPost(
            TweetId: "tweet-3",
            Url: "https://x.com/u/status/3",
            Title: longTitle,
            SummaryHtml: string.Empty,
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Equal(3903, result.Caption.Length);
        Assert.EndsWith("...", result.Caption, StringComparison.Ordinal);
    }
}
