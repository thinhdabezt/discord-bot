using System.Text.RegularExpressions;
using System.ServiceModel.Syndication;
using System.Xml;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordXBot.Configuration;
using DiscordXBot.Data;
using DiscordXBot.Data.Entities;
using DiscordXBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DiscordXBot.Discord.Commands;

public sealed class InstagramFeedModule(
    BotDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<RetryOptions> retryOptions,
    FeedUrlResolver feedUrlResolver,
    ILogger<InstagramFeedModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptions<RetryOptions> _retryOptions = retryOptions;
    private readonly FeedUrlResolver _feedUrlResolver = feedUrlResolver;
    private readonly ILogger<InstagramFeedModule> _logger = logger;

    [SlashCommand("add-ig", "Add an Instagram username feed to a Discord channel")]
    public async Task AddInstagramAsync(string username, ITextChannel channel)
    {
        if (!TryValidateGuildContext(out var guildUser))
        {
            await ReplyAsync("This command can only be used inside a guild.");
            return;
        }

        if (!HasManagementPermission(guildUser))
        {
            await ReplyAsync("You need Manage Server/Manage Channels permission to use this command.");
            return;
        }

        if (channel.GuildId != Context.Guild.Id)
        {
            await ReplyAsync("Target channel must belong to this guild.");
            return;
        }

        var normalizedUsername = NormalizeInstagramUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            await ReplyAsync("Invalid Instagram username.");
            return;
        }

        await EnsureDeferredAsync();

        var provider = _feedUrlResolver.GetDefaultProvider(FeedPlatform.Instagram);
        if (!_feedUrlResolver.IsProviderEnabled(provider))
        {
            await ReplyAsync($"Provider {provider} is disabled by configuration.");
            return;
        }

        string rssUrl;
        try
        {
            rssUrl = _feedUrlResolver.Resolve(FeedPlatform.Instagram, provider, normalizedUsername);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Instagram RSS URL for @{Username} with provider {Provider}", normalizedUsername, provider);
            await ReplyAsync("Unable to construct RSS URL from current provider settings.");
            return;
        }

        if (!await ValidateRssAsync(rssUrl))
        {
            _logger.LogInformation("add-ig validation failed for @{Username}", normalizedUsername);
            await ReplyAsync($"Unable to validate Instagram feed for @{normalizedUsername}. Check username or RSS-Bridge InstagramBridge access.");
            return;
        }

        var guildId = unchecked((long)Context.Guild.Id);
        var channelId = unchecked((long)channel.Id);

        var existing = await _db.TrackedFeeds.FirstOrDefaultAsync(x =>
            x.GuildId == guildId &&
            x.ChannelId == channelId &&
            x.Platform == FeedPlatform.Instagram &&
            x.SourceKey == normalizedUsername);

        if (existing is not null)
        {
            await ReplyAsync($"@{normalizedUsername} is already tracked in {channel.Mention}.");
            return;
        }

        _db.TrackedFeeds.Add(new TrackedFeed
        {
            GuildId = guildId,
            ChannelId = channelId,
            XUsername = BuildLegacyUsername(normalizedUsername),
            SourceKey = normalizedUsername,
            Platform = FeedPlatform.Instagram,
            Provider = provider,
            RssUrl = rssUrl,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation(ex, "Duplicate Instagram feed mapping prevented for @{Username}", normalizedUsername);
            await ReplyAsync($"@{normalizedUsername} is already tracked in {channel.Mention}.");
            return;
        }

        await ReplyAsync($"Added Instagram @{normalizedUsername} to {channel.Mention}.");
    }

    [SlashCommand("list-ig", "List tracked Instagram feeds in this guild")]
    public async Task ListInstagramAsync()
    {
        if (!TryValidateGuildContext(out _))
        {
            await ReplyAsync("This command can only be used inside a guild.");
            return;
        }

        await EnsureDeferredAsync();

        var guildId = unchecked((long)Context.Guild.Id);
        var feeds = await _db.TrackedFeeds
            .AsNoTracking()
            .Where(x => x.GuildId == guildId && x.IsActive && x.Platform == FeedPlatform.Instagram)
            .OrderBy(x => x.SourceKey)
            .ThenBy(x => x.ChannelId)
            .ToListAsync();

        if (feeds.Count == 0)
        {
            await ReplyAsync("No Instagram feeds are currently tracked in this guild.");
            return;
        }

        var lines = feeds
            .Select(x => $"- @{x.SourceKey} -> <#{x.ChannelId}> ({x.Provider})")
            .Take(25)
            .ToList();

        if (feeds.Count > lines.Count)
        {
            lines.Add($"... and {feeds.Count - lines.Count} more");
        }

        var embed = new EmbedBuilder()
            .WithTitle("Tracked Instagram Feeds")
            .WithColor(new Color(225, 48, 108))
            .WithDescription(string.Join("\n", lines))
            .WithCurrentTimestamp()
            .Build();

        await ReplyAsync(string.Empty, embed);
    }

    [SlashCommand("remove-ig", "Remove tracked Instagram feed mapping")]
    public async Task RemoveInstagramAsync(string username, ITextChannel? channel = null)
    {
        if (!TryValidateGuildContext(out var guildUser))
        {
            await ReplyAsync("This command can only be used inside a guild.");
            return;
        }

        if (!HasManagementPermission(guildUser))
        {
            await ReplyAsync("You need Manage Server/Manage Channels permission to use this command.");
            return;
        }

        var normalizedUsername = NormalizeInstagramUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            await ReplyAsync("Invalid Instagram username.");
            return;
        }

        await EnsureDeferredAsync();

        var guildId = unchecked((long)Context.Guild.Id);
        var query = _db.TrackedFeeds.Where(x =>
            x.GuildId == guildId &&
            x.Platform == FeedPlatform.Instagram &&
            x.SourceKey == normalizedUsername);

        if (channel is not null)
        {
            if (channel.GuildId != Context.Guild.Id)
            {
                await ReplyAsync("Target channel must belong to this guild.");
                return;
            }

            query = query.Where(x => x.ChannelId == unchecked((long)channel.Id));
        }

        var feeds = await query.ToListAsync();
        if (feeds.Count == 0)
        {
            await ReplyAsync($"No tracked mapping found for Instagram @{normalizedUsername}.");
            return;
        }

        _db.TrackedFeeds.RemoveRange(feeds);
        await _db.SaveChangesAsync();

        await ReplyAsync($"Removed {feeds.Count} mapping(s) for Instagram @{normalizedUsername}.");
    }

    internal static string NormalizeInstagramUsername(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (segments.Count == 0)
            {
                return string.Empty;
            }

            if (segments[0].Equals("p", StringComparison.OrdinalIgnoreCase) ||
                segments[0].Equals("reel", StringComparison.OrdinalIgnoreCase) ||
                segments[0].Equals("reels", StringComparison.OrdinalIgnoreCase) ||
                segments[0].Equals("stories", StringComparison.OrdinalIgnoreCase) ||
                segments[0].Equals("explore", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            value = segments[0];
        }

        if (value.StartsWith('@'))
        {
            value = value[1..];
        }

        value = value.ToLowerInvariant();
        value = Regex.Replace(value, "[^a-z0-9._]", string.Empty);
        if (value.Length is < 1 or > 30)
        {
            return string.Empty;
        }

        return value;
    }

    private async Task<bool> ValidateRssAsync(string rssUrl)
    {
        var maxRetries = Math.Max(0, _retryOptions.Value.MaxRetries);
        var initialDelaySeconds = Math.Max(1, _retryOptions.Value.InitialDelaySeconds);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var client = _httpClientFactory.CreateClient();
                using var response = await client.GetAsync(rssUrl, HttpCompletionOption.ResponseContentRead, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cts.Token);
                    if (string.IsNullOrWhiteSpace(body) || LooksLikeBridgeErrorPayload(body))
                    {
                        return false;
                    }

                    return HasUsableFeedItems(body);
                }

                if ((int)response.StatusCode >= 500 && attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, attempt));
                    await Task.Delay(delay);
                    continue;
                }

                return false;
            }
            catch (OperationCanceledException) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, attempt));
                await Task.Delay(delay);
            }
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, attempt));
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Instagram RSS validation failed for URL {RssUrl}", rssUrl);
                return false;
            }
        }

        return false;
    }

    private static bool LooksLikeBridgeErrorPayload(string payload)
    {
        return payload.Contains("Bridge returned error", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("404 Page Not Found", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("RSS-Bridge tried to fetch a page", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("HttpException", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableFeedItems(string payload)
    {
        try
        {
            using var textReader = new StringReader(payload);
            using var xmlReader = XmlReader.Create(textReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            var feed = SyndicationFeed.Load(xmlReader);
            return feed?.Items?.Any() == true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReplyAsync(string message, Embed? embed = null, bool ephemeral = true)
    {
        if (Context.Interaction.HasResponded)
        {
            if (embed is null)
            {
                await FollowupAsync(message, ephemeral: ephemeral);
            }
            else
            {
                await FollowupAsync(message, embed: embed, ephemeral: ephemeral);
            }

            return;
        }

        if (embed is null)
        {
            await RespondAsync(message, ephemeral: ephemeral);
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            await RespondAsync(embed: embed, ephemeral: ephemeral);
            return;
        }

        await RespondAsync(message, embed: embed, ephemeral: ephemeral);
    }

    private async Task EnsureDeferredAsync(bool ephemeral = true)
    {
        if (Context.Interaction.HasResponded)
        {
            return;
        }

        await DeferAsync(ephemeral: ephemeral);
    }

    private bool TryValidateGuildContext(out SocketGuildUser guildUser)
    {
        if (Context.Guild is null || Context.User is not SocketGuildUser user)
        {
            guildUser = null!;
            return false;
        }

        guildUser = user;
        return true;
    }

    private static bool HasManagementPermission(SocketGuildUser guildUser)
    {
        var permissions = guildUser.GuildPermissions;
        return permissions.Administrator || permissions.ManageGuild || permissions.ManageChannels;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: "23505" };
    }

    private static string BuildLegacyUsername(string username)
    {
        var normalized = Regex.Replace(username.ToLowerInvariant(), "[^a-z0-9_]", string.Empty);
        if (normalized.Length == 0)
        {
            normalized = "source";
        }

        if (normalized.Length > 61)
        {
            normalized = normalized[..61];
        }

        return $"ig_{normalized}";
    }
}
