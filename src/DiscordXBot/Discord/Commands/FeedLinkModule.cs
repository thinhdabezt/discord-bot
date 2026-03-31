using System.Security.Cryptography;
using System.Text;
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

public sealed class FeedLinkModule(
    BotDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<RetryOptions> retryOptions,
    FeedUrlResolver feedUrlResolver,
    ILogger<FeedLinkModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptions<RetryOptions> _retryOptions = retryOptions;
    private readonly FeedUrlResolver _feedUrlResolver = feedUrlResolver;
    private readonly ILogger<FeedLinkModule> _logger = logger;

    [SlashCommand("add-link", "Add a direct RSS link (FetchRSS or any RSS URL)")]
    public async Task AddLinkAsync(string rssUrl, string platform, ITextChannel channel)
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

        if (!TryParsePlatform(platform, out var parsedPlatform))
        {
            await ReplyAsync("Unsupported platform. Use: X or FB.");
            return;
        }

        if (!_feedUrlResolver.IsProviderEnabled(FeedProvider.DirectRss))
        {
            await ReplyAsync("Direct RSS provider is disabled by configuration.");
            return;
        }

        await EnsureDeferredAsync();

        string resolvedUrl;
        try
        {
            resolvedUrl = _feedUrlResolver.Resolve(parsedPlatform, FeedProvider.DirectRss, rssUrl);
        }
        catch
        {
            await ReplyAsync("Invalid RSS URL. Please provide an absolute http/https URL.");
            return;
        }

        if (!await ValidateRssAsync(resolvedUrl))
        {
            await ReplyAsync("Unable to validate this RSS URL.");
            return;
        }

        var sourceKey = BuildSourceKey(resolvedUrl);
        var guildId = unchecked((long)Context.Guild.Id);
        var channelId = unchecked((long)channel.Id);

        var existing = await _db.TrackedFeeds.FirstOrDefaultAsync(x =>
            x.GuildId == guildId &&
            x.ChannelId == channelId &&
            x.Platform == parsedPlatform &&
            x.SourceKey == sourceKey);

        if (existing is not null)
        {
            await ReplyAsync("This RSS link is already tracked in the target channel.");
            return;
        }

        _db.TrackedFeeds.Add(new TrackedFeed
        {
            GuildId = guildId,
            ChannelId = channelId,
            XUsername = BuildLegacyUsername(sourceKey, parsedPlatform),
            SourceKey = sourceKey,
            Platform = parsedPlatform,
            Provider = FeedProvider.DirectRss,
            RssUrl = resolvedUrl,
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
            _logger.LogInformation(ex, "Duplicate direct RSS mapping prevented for {SourceKey}", sourceKey);
            await ReplyAsync("This RSS link is already tracked in the target channel.");
            return;
        }

        await ReplyAsync($"Added direct RSS feed for {parsedPlatform} to {channel.Mention}.");
    }

    [SlashCommand("list-links", "List direct RSS links in this guild")]
    public async Task ListLinksAsync()
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
            .Where(x => x.GuildId == guildId && x.IsActive && x.Provider == FeedProvider.DirectRss)
            .OrderBy(x => x.Platform)
            .ThenBy(x => x.SourceKey)
            .ThenBy(x => x.ChannelId)
            .ToListAsync();

        if (feeds.Count == 0)
        {
            await ReplyAsync("No direct RSS links are currently tracked in this guild.");
            return;
        }

        var lines = feeds
            .Select(x => $"- [{x.Platform}] {TrimForList(x.SourceKey)} -> <#{x.ChannelId}>")
            .Take(25)
            .ToList();

        if (feeds.Count > lines.Count)
        {
            lines.Add($"... and {feeds.Count - lines.Count} more");
        }

        var embed = new EmbedBuilder()
            .WithTitle("Tracked Direct RSS Links")
            .WithColor(new Color(40, 167, 69))
            .WithDescription(string.Join("\n", lines))
            .WithCurrentTimestamp()
            .Build();

        await ReplyAsync(string.Empty, embed);
    }

    [SlashCommand("remove-link", "Remove tracked direct RSS link")]
    public async Task RemoveLinkAsync(string rssUrl, ITextChannel? channel = null)
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

        await EnsureDeferredAsync();

        var guildId = unchecked((long)Context.Guild.Id);
        var normalizedUrl = rssUrl.Trim();

        var query = _db.TrackedFeeds.Where(x =>
            x.GuildId == guildId &&
            x.Provider == FeedProvider.DirectRss &&
            x.RssUrl == normalizedUrl);

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
            await ReplyAsync("No tracked direct RSS mapping found for this URL.");
            return;
        }

        _db.TrackedFeeds.RemoveRange(feeds);
        await _db.SaveChangesAsync();

        await ReplyAsync($"Removed {feeds.Count} direct RSS mapping(s).");
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
                using var response = await client.GetAsync(rssUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return true;
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
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryParsePlatform(string value, out FeedPlatform platform)
    {
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "x":
            case "twitter":
                platform = FeedPlatform.X;
                return true;
            case "fb":
            case "facebook":
                platform = FeedPlatform.Facebook;
                return true;
            default:
                platform = FeedPlatform.X;
                return false;
        }
    }

    private static string BuildSourceKey(string rssUrl)
    {
        var value = rssUrl.Trim();
        if (value.Length <= 240)
        {
            return value;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
        return value[..220] + "#" + hash;
    }

    private static string BuildLegacyUsername(string sourceKey, FeedPlatform platform)
    {
        var prefix = platform == FeedPlatform.Facebook ? "fb" : "x";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey))).ToLowerInvariant()[..20];
        return $"{prefix}_link_{hash}";
    }

    private static string TrimForList(string value)
    {
        if (value.Length <= 80)
        {
            return value;
        }

        return value[..77] + "...";
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
}
