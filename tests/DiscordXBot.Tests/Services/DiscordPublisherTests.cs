using DiscordXBot.Services;
using DiscordXBot.Data.Entities;

namespace DiscordXBot.Tests.Services;

public class DiscordPublisherTests
{
    [Fact]
    public void BuildMessageText_FormatsPlainTextWithUtcAndLink()
    {
        var postedAtUtc = new DateTime(2026, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.X,
            SourceKey = "medrives1338",
            XUsername = "medrives1338"
        };

        var text = DiscordPublisher.BuildMessageText(
            feed: feed,
            caption: "Hello world",
            postedAtUtc: postedAtUtc,
            postUrl: "https://x.com/medrives1338/status/1");

        Assert.Equal(
            "X: @medrives1338\n" +
            "Caption: Hello world\n" +
            "Posted: 2026-01-02 03:04:05 UTC\n" +
            "Link: https://x.com/medrives1338/status/1",
            text);
    }

    [Fact]
    public void BuildMessageText_UsesFallbacksForEmptyCaptionAndMissingLink()
    {
        var postedAtUtc = new DateTime(2026, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.X,
            SourceKey = "medrives1338",
            XUsername = "medrives1338"
        };

        var text = DiscordPublisher.BuildMessageText(
            feed: feed,
            caption: "   ",
            postedAtUtc: postedAtUtc,
            postUrl: null);

        Assert.Equal(
            "X: @medrives1338\n" +
            "Caption: (empty)\n" +
            "Posted: 2026-01-02 03:04:05 UTC\n" +
            "Link: N/A",
            text);
    }

    [Fact]
    public void BuildMessageText_TruncatesVeryLongOutput()
    {
        var postedAtUtc = new DateTime(2026, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var longCaption = new string('x', 4000);
        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.X,
            SourceKey = "medrives1338",
            XUsername = "medrives1338"
        };

        var text = DiscordPublisher.BuildMessageText(
            feed: feed,
            caption: longCaption,
            postedAtUtc: postedAtUtc,
            postUrl: "https://x.com/medrives1338/status/1");

        Assert.True(text.Length <= 1903);
        Assert.EndsWith("...", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessageText_FormatsFacebookFanpageHeader()
    {
        var postedAtUtc = new DateTime(2026, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.Facebook,
            SourceKey = "nasa"
        };

        var text = DiscordPublisher.BuildMessageText(
            feed: feed,
            caption: "Fanpage update",
            postedAtUtc: postedAtUtc,
            postUrl: "https://facebook.com/nasa/posts/1");

        Assert.Equal(
            "FB Fanpage: nasa\n" +
            "Caption: Fanpage update\n" +
            "Posted: 2026-01-02 03:04:05 UTC\n" +
            "Link: https://facebook.com/nasa/posts/1",
            text);
    }

    [Fact]
    public void BuildMessageText_FormatsFacebookProfileHeader()
    {
        var postedAtUtc = new DateTime(2026, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var feed = new TrackedFeed
        {
            Platform = FeedPlatform.Facebook,
            SourceType = FacebookSourceType.Profile,
            SourceKey = "10001234567890"
        };

        var text = DiscordPublisher.BuildMessageText(
            feed: feed,
            caption: "Profile update",
            postedAtUtc: postedAtUtc,
            postUrl: "https://facebook.com/10001234567890/posts/1");

        Assert.Equal(
            "FB Profile: 10001234567890\n" +
            "Caption: Profile update\n" +
            "Posted: 2026-01-02 03:04:05 UTC\n" +
            "Link: https://facebook.com/10001234567890/posts/1",
            text);
    }

    [Fact]
    public void NormalizePostUrlForDisplay_ConvertsNitterStatusToXCom()
    {
        var normalized = DiscordPublisher.NormalizePostUrlForDisplay(
            "https://nitter.net/MeDrives1338/status/2039116050038460527#m");

        Assert.Equal(
            "https://x.com/MeDrives1338/status/2039116050038460527",
            normalized);
    }

    [Fact]
    public void NormalizePostUrlForDisplay_KeepsNonNitterUrl()
    {
        var normalized = DiscordPublisher.NormalizePostUrlForDisplay(
            "https://x.com/medrives1338/status/1");

        Assert.Equal("https://x.com/medrives1338/status/1", normalized);
    }
}
