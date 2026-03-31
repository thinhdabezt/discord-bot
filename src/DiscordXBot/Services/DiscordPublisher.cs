using Discord;
using Discord.Net;
using Discord.WebSocket;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services.Models;
using Microsoft.Extensions.Options;
using DiscordXBot.Configuration;
using System.Globalization;

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
        var channelId = unchecked((ulong)feed.ChannelId);
        var channel = _client.GetChannel(channelId) as IMessageChannel;

        if (channel is null)
        {
            var requestOptions = new RequestOptions { CancelToken = cancellationToken };
            channel = await _client.GetChannelAsync(channelId, requestOptions) as IMessageChannel;
        }

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
            var sanitizedPostUrl = DiscordUrlSanitizer.Sanitize(post.Url);
            if (sanitizedPostUrl is null && !string.IsNullOrWhiteSpace(post.Url))
            {
                _logger.LogWarning(
                    "Skipping invalid post URL for tweet {TweetId}: {PostUrl}",
                    post.TweetId,
                    post.Url);
            }

            var sanitizedImages = content.ImageUrls
                .Select(DiscordUrlSanitizer.Sanitize)
                .Where(x => x is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var droppedImageCount = content.ImageUrls.Count - sanitizedImages.Count;
            if (droppedImageCount > 0)
            {
                _logger.LogWarning(
                    "Dropped {DroppedCount} invalid image URL(s) for tweet {TweetId}",
                    droppedImageCount,
                    post.TweetId);
            }

            var messageText = BuildMessageText(
                feed.XUsername,
                content.Caption,
                content.PostedAtUtc,
                sanitizedPostUrl);

            var requestOptions = new RequestOptions { CancelToken = cancellationToken };
            try
            {
                if (sanitizedImages.Count > 0)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(new Color(29, 161, 242))
                        .WithImageUrl(sanitizedImages[0])
                        .Build();

                    await channel.SendMessageAsync(text: messageText, embed: embed, options: requestOptions);
                }
                else
                {
                    await channel.SendMessageAsync(text: messageText, options: requestOptions);
                }
            }
            catch (HttpException ex) when (IsInvalidUrlPayload(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Invalid URL payload for tweet {TweetId}. Falling back to text-only publish. PostUrl={PostUrl}, FirstImage={FirstImage}",
                    post.TweetId,
                    sanitizedPostUrl,
                    sanitizedImages.FirstOrDefault());

                await channel.SendMessageAsync(text: messageText, options: requestOptions);

                // Main message fallback already sent; skip additional image embeds for this post.
                sanitizedImages.Clear();
            }

            var maxAdditionalImages = Math.Max(0, _publishOptions.CurrentValue.MaxAdditionalImages);

            // Send additional images as separate embeds to preserve ordering and avoid giant messages.
            foreach (var image in sanitizedImages.Skip(1).Take(maxAdditionalImages))
            {
                var extraEmbed = new EmbedBuilder()
                    .WithColor(new Color(29, 161, 242))
                    .WithImageUrl(image)
                    .Build();

                try
                {
                    await channel.SendMessageAsync(embed: extraEmbed, options: requestOptions);
                }
                catch (HttpException ex) when (IsInvalidUrlPayload(ex))
                {
                    _logger.LogWarning(
                        ex,
                        "Skipping invalid additional image URL for tweet {TweetId}: {ImageUrl}",
                        post.TweetId,
                        image);
                }
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

    private static bool IsInvalidUrlPayload(HttpException ex)
    {
        return (int)ex.HttpCode == 400 &&
               ex.Message.Contains("URL_TYPE_INVALID_URL", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildMessageText(string username, string caption, DateTime postedAtUtc, string? postUrl)
    {
        var safeCaption = string.IsNullOrWhiteSpace(caption) ? "(empty)" : caption.Trim();
        var safeLink = string.IsNullOrWhiteSpace(postUrl) ? "N/A" : postUrl;

        var text = string.Join(
            "\n",
            $"X: @{username}",
            $"Caption: {safeCaption}",
            $"Posted: {postedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}",
            $"Link: {safeLink}");

        if (text.Length > 1900)
        {
            return text[..1900] + "...";
        }

        return text;
    }
}
