using Discord;
using Discord.WebSocket;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services.Models;

namespace DiscordXBot.Services;

public sealed class DiscordPublisher(DiscordSocketClient client, ILogger<DiscordPublisher> logger)
{
    private readonly DiscordSocketClient _client = client;
    private readonly ILogger<DiscordPublisher> _logger = logger;

    public async Task<bool> PublishAsync(TrackedFeed feed, RssPost post, ParsedTweetContent content, CancellationToken cancellationToken)
    {
        var channel = _client.GetChannel(unchecked((ulong)feed.ChannelId)) as IMessageChannel;
        if (channel is null)
        {
            _logger.LogWarning(
                "Channel {ChannelId} not found for guild {GuildId}. Skipping publish.",
                feed.ChannelId,
                feed.GuildId);
            return false;
        }

        var embedBuilder = new EmbedBuilder()
            .WithAuthor($"@{feed.XUsername}")
            .WithDescription(content.Caption)
            .WithColor(new Color(29, 161, 242))
            .WithCurrentTimestamp();

        if (!string.IsNullOrWhiteSpace(post.Url))
        {
            embedBuilder.WithUrl(post.Url);
        }

        if (content.ImageUrls.Count > 0)
        {
            embedBuilder.WithImageUrl(content.ImageUrls[0]);
        }

        await channel.SendMessageAsync(embed: embedBuilder.Build());

        // Send additional images as separate embeds to preserve ordering and avoid giant messages.
        foreach (var image in content.ImageUrls.Skip(1).Take(3))
        {
            var extraEmbed = new EmbedBuilder()
                .WithColor(new Color(29, 161, 242))
                .WithImageUrl(image)
                .Build();

            await channel.SendMessageAsync(embed: extraEmbed);
        }

        _logger.LogInformation(
            "Published tweet {TweetId} to channel {ChannelId} for @{Username}",
            post.TweetId,
            feed.ChannelId,
            feed.XUsername);

        return true;
    }
}
