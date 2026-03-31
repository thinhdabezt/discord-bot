using DiscordXBot.Data;
using DiscordXBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordXBot.Tests.Data;

public class ProcessedTweetDedupeTests
{
    [Fact]
    public async Task ExistingMarkerInSameFeed_IsDetectedAsDuplicate()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new BotDbContext(options);

        var feed = new TrackedFeed
        {
            GuildId = 1,
            ChannelId = 10,
            XUsername = "tester",
            RssUrl = "http://rss"
        };

        db.TrackedFeeds.Add(feed);
        await db.SaveChangesAsync();

        var marker = new ProcessedTweet
        {
            TrackedFeedId = feed.Id,
            GuildId = 1,
            ChannelId = 10,
            XUsername = "tester",
            TweetId = "tweet-1",
            TweetUrl = "https://x.com/status/1"
        };

        db.ProcessedTweets.Add(marker);
        await db.SaveChangesAsync();

        var duplicateExists = await db.ProcessedTweets.AnyAsync(x =>
            x.TrackedFeedId == feed.Id && x.TweetId == "tweet-1");

        Assert.True(duplicateExists);
    }

    [Fact]
    public async Task SameTweetAcrossDifferentFeeds_IsAllowed()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new BotDbContext(options);

        var feed1 = new TrackedFeed
        {
            GuildId = 1,
            ChannelId = 10,
            XUsername = "tester",
            RssUrl = "http://rss"
        };

        var feed2 = new TrackedFeed
        {
            GuildId = 2,
            ChannelId = 20,
            XUsername = "tester",
            RssUrl = "http://rss"
        };

        db.TrackedFeeds.AddRange(feed1, feed2);
        await db.SaveChangesAsync();

        db.ProcessedTweets.AddRange(
            new ProcessedTweet
            {
                TrackedFeedId = feed1.Id,
                GuildId = 1,
                ChannelId = 10,
                XUsername = "tester",
                TweetId = "tweet-1",
                TweetUrl = "https://x.com/status/1"
            },
            new ProcessedTweet
            {
                TrackedFeedId = feed2.Id,
                GuildId = 2,
                ChannelId = 20,
                XUsername = "tester",
                TweetId = "tweet-1",
                TweetUrl = "https://x.com/status/1"
            });

        await db.SaveChangesAsync();

        var count = await db.ProcessedTweets.CountAsync(x => x.TweetId == "tweet-1");
        Assert.Equal(2, count);

        var sameFeedDuplicate = await db.ProcessedTweets.AnyAsync(x =>
            x.TrackedFeedId == feed1.Id && x.TweetId == "tweet-1");

        var secondFeedDuplicate = await db.ProcessedTweets.AnyAsync(x =>
            x.TrackedFeedId == feed2.Id && x.TweetId == "tweet-1");

        Assert.True(sameFeedDuplicate);
        Assert.True(secondFeedDuplicate);
    }
}
