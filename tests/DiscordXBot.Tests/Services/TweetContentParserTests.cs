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
        Assert.Equal(ParsedMediaType.ImageOnly, result.MediaType);
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
        Assert.Equal(ParsedMediaType.CaptionOnly, result.MediaType);
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
        Assert.Equal(ParsedMediaType.CaptionOnly, result.MediaType);
    }

    [Fact]
    public void Parse_FiltersInvalidImageUrls()
    {
        var post = new RssPost(
            TweetId: "tweet-4",
            Url: "https://x.com/u/status/4",
            Title: "Caption",
            SummaryHtml: "<img src='not-a-url'/><img src='ftp://example.com/a.jpg'/><img src='https://pbs.twimg.com/media/c.jpg:small'/>",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Single(result.ImageUrls);
        Assert.Equal("https://pbs.twimg.com/media/c.jpg:orig", result.ImageUrls[0]);
        Assert.Equal(ParsedMediaType.ImageOnly, result.MediaType);
    }

    [Fact]
    public void Parse_FiltersInternalHostImageUrls()
    {
        var post = new RssPost(
            TweetId: "tweet-5",
            Url: "https://x.com/u/status/5",
            Title: "Caption",
            SummaryHtml: "<img src='http://rss-bridge/image.jpg'/><img src='https://pbs.twimg.com/media/d.jpg:small'/>",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Single(result.ImageUrls);
        Assert.Equal("https://pbs.twimg.com/media/d.jpg:orig", result.ImageUrls[0]);
        Assert.Equal(ParsedMediaType.ImageOnly, result.MediaType);
    }

    [Fact]
    public void Parse_NormalizesNitterProxyImageUrlsToPbs()
    {
        var post = new RssPost(
            TweetId: "tweet-6",
            Url: "https://nitter.net/MeDrives1338/status/2039103555311689847#m",
            Title: "Caption",
            SummaryHtml: "<img src='https://nitter.net/pic/media%2FHExZ3ZObQAAUBTK.jpg' />",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Single(result.ImageUrls);
        Assert.Equal("https://pbs.twimg.com/media/HExZ3ZObQAAUBTK.jpg", result.ImageUrls[0]);
        Assert.Equal(ParsedMediaType.ImageOnly, result.MediaType);
    }

    [Fact]
    public void Parse_AllowsImageOnlyPostWithoutCaption()
    {
        var post = new RssPost(
            TweetId: "tweet-6b",
            Url: "https://x.com/u/status/6b",
            Title: string.Empty,
            SummaryHtml: "<img src='https://pbs.twimg.com/media/only-image.jpg:small' />",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Equal("https://x.com/u/status/6b", result.Caption);
        Assert.Single(result.ImageUrls);
        Assert.Equal("https://pbs.twimg.com/media/only-image.jpg:orig", result.ImageUrls[0]);
        Assert.Equal(ParsedMediaType.ImageOnly, result.MediaType);
    }

    [Fact]
    public void Parse_ClassifiesGifOnlyPosts()
    {
        var post = new RssPost(
            TweetId: "tweet-7",
            Url: "https://x.com/u/status/7",
            Title: "Gif post",
            SummaryHtml: "<img src='https://pbs.twimg.com/media/loop.gif' />",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Empty(result.ImageUrls);
        Assert.Equal(ParsedMediaType.GifOnly, result.MediaType);
    }

    [Fact]
    public void Parse_ClassifiesVideoOnlyPosts()
    {
        var post = new RssPost(
            TweetId: "tweet-8",
            Url: "https://x.com/u/status/8",
            Title: "Video post",
            SummaryHtml: "<video src='https://video.twimg.com/ext_tw_video/1234/pu/vid/1280x720/file.mp4'></video>",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Empty(result.ImageUrls);
        Assert.Equal(ParsedMediaType.VideoOnly, result.MediaType);
    }

    [Fact]
    public void Parse_ClassifiesMixedPosts()
    {
        var post = new RssPost(
            TweetId: "tweet-9",
            Url: "https://x.com/u/status/9",
            Title: "Mixed post",
            SummaryHtml: "<img src='https://pbs.twimg.com/media/e.jpg:small' /><a href='https://video.twimg.com/ext_tw_video/1/file.mp4'>v</a>",
            PublishedAtUtc: DateTime.UtcNow);

        var result = _parser.Parse(post);

        Assert.Single(result.ImageUrls);
        Assert.Equal(ParsedMediaType.Mixed, result.MediaType);
    }

    [Fact]
    public void Parse_PreservesPublishedUtcTimestamp()
    {
        var publishedAtUtc = new DateTime(2026, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var post = new RssPost(
            TweetId: "tweet-10",
            Url: "https://x.com/u/status/10",
            Title: "Caption",
            SummaryHtml: "<p>hello</p>",
            PublishedAtUtc: publishedAtUtc);

        var result = _parser.Parse(post);

        Assert.Equal(publishedAtUtc, result.PostedAtUtc);
    }
}
