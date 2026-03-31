using DiscordXBot.Services;

namespace DiscordXBot.Tests.Services;

public class DiscordPublisherTests
{
    [Fact]
    public void BuildMessageText_FormatsPlainTextWithUtcAndLink()
    {
        var postedAtUtc = new DateTime(2026, 01, 02, 03, 04, 05, DateTimeKind.Utc);

        var text = DiscordPublisher.BuildMessageText(
            username: "medrives1338",
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

        var text = DiscordPublisher.BuildMessageText(
            username: "medrives1338",
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

        var text = DiscordPublisher.BuildMessageText(
            username: "medrives1338",
            caption: longCaption,
            postedAtUtc: postedAtUtc,
            postUrl: "https://x.com/medrives1338/status/1");

        Assert.True(text.Length <= 1903);
        Assert.EndsWith("...", text, StringComparison.Ordinal);
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
