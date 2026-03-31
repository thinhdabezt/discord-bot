using Discord;
using Discord.Net;
using Discord.WebSocket;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services.Models;
using Microsoft.Extensions.Options;
using DiscordXBot.Configuration;

namespace DiscordXBot.Services;

public sealed class DiscordPublisher(
    DiscordSocketClient client,
    IOptionsMonitor<PublishOptions> publishOptions,
    ILogger<DiscordPublisher> logger)
{
    private readonly DiscordSocketClient _client = client;
    private readonly IOptionsMonitor<PublishOptions> _publishOptions = publishOptions;
    private readonly ILogger<DiscordPublisher> _logger = logger;

    public async Task<PublishResult> PublishAsync(
        TrackedFeed feed,
        RssPost post,
        ParsedTweetContent content,
        CancellationToken cancellationToken)
    {
        var channel = _client.GetChannel(unchecked((ulong)feed.ChannelId)) as IMessageChannel;
        if (channel is null)
        {
            _logger.LogWarning(
                "Channel {ChannelId} not found for guild {GuildId}. Skipping publish.",
                feed.ChannelId,
                feed.GuildId);
            return PublishResult.Fail(PublishFailureKind.ChannelMissing, "Channel not found");
        }

        try
        {
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

            var requestOptions = new RequestOptions { CancelToken = cancellationToken };
            await channel.SendMessageAsync(embed: embedBuilder.Build(), options: requestOptions);

            var maxAdditionalImages = Math.Max(0, _publishOptions.CurrentValue.MaxAdditionalImages);

            // Send additional images as separate embeds to preserve ordering and avoid giant messages.
            foreach (var image in content.ImageUrls.Skip(1).Take(maxAdditionalImages))
            {
                var extraEmbed = new EmbedBuilder()
                    .WithColor(new Color(29, 161, 242))
                    .WithImageUrl(image)
                    .Build();

                await channel.SendMessageAsync(embed: extraEmbed, options: requestOptions);
            }

            _logger.LogInformation(
                "Published tweet {TweetId} to channel {ChannelId} for @{Username}",
                post.TweetId,
                feed.ChannelId,
                feed.XUsername);

            return PublishResult.Ok();
        }
        catch (HttpException ex) when ((int)ex.HttpCode == 429)
        {
            _logger.LogWarning(
                ex,
                "Discord rate limit hit while publishing tweet {TweetId} to channel {ChannelId}",
                post.TweetId,
                feed.ChannelId);
            return PublishResult.Fail(PublishFailureKind.RateLimited, ex.Message);
        }
        catch (HttpException ex) when ((int)ex.HttpCode >= 500)
        {
            _logger.LogWarning(
                ex,
                "Discord transient server error while publishing tweet {TweetId} to channel {ChannelId}",
                post.TweetId,
                feed.ChannelId);
            return PublishResult.Fail(PublishFailureKind.Transient, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Network error while publishing tweet {TweetId} to channel {ChannelId}",
                post.TweetId,
                feed.ChannelId);
            return PublishResult.Fail(PublishFailureKind.Transient, ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Timeout while publishing tweet {TweetId} to channel {ChannelId}",
                post.TweetId,
                feed.ChannelId);
            return PublishResult.Fail(PublishFailureKind.Transient, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fatal publish error for tweet {TweetId} to channel {ChannelId}",
                post.TweetId,
                feed.ChannelId);
            return PublishResult.Fail(PublishFailureKind.Fatal, ex.Message);
        }
    }
}
