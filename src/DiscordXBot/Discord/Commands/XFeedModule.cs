using System.Text.RegularExpressions;
using System.Net.Http;
using DiscordXBot.Configuration;
using DiscordXBot.Data;
using DiscordXBot.Data.Entities;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Discord.Commands;

public sealed class XFeedModule(
    BotDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<RssBridgeOptions> rssBridgeOptions,
    ILogger<XFeedModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptions<RssBridgeOptions> _rssBridgeOptions = rssBridgeOptions;
    private readonly ILogger<XFeedModule> _logger = logger;

    [SlashCommand("add-x", "Add an X account feed to a Discord channel")]
    public async Task AddXAsync(string username, ITextChannel channel)
    {
        if (!TryValidateGuildContext(out var guildUser))
        {
            await RespondAsync("This command can only be used inside a guild.", ephemeral: true);
            return;
        }

        if (!HasManagementPermission(guildUser))
        {
            await RespondAsync("You need Manage Server/Manage Channels permission to use this command.", ephemeral: true);
            return;
        }

        if (channel.GuildId != Context.Guild.Id)
        {
            await RespondAsync("Target channel must belong to this guild.", ephemeral: true);
            return;
        }

        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrEmpty(normalizedUsername))
        {
            await RespondAsync("Invalid X username.", ephemeral: true);
            return;
        }

        var rssUrl = BuildRssUrl(normalizedUsername);
        if (!await ValidateRssAsync(rssUrl))
        {
            await RespondAsync($"Unable to validate feed for @{normalizedUsername}. Check username or RSS-Bridge settings.", ephemeral: true);
            return;
        }

        var guildId = unchecked((long)Context.Guild.Id);
        var channelId = unchecked((long)channel.Id);

        var existing = await _db.TrackedFeeds.FirstOrDefaultAsync(x =>
            x.GuildId == guildId && x.ChannelId == channelId && x.XUsername == normalizedUsername);

        if (existing is not null)
        {
            await RespondAsync($"@{normalizedUsername} is already tracked in {channel.Mention}.", ephemeral: true);
            return;
        }

        _db.TrackedFeeds.Add(new TrackedFeed
        {
            GuildId = guildId,
            ChannelId = channelId,
            XUsername = normalizedUsername,
            RssUrl = rssUrl,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        await RespondAsync($"Added @{normalizedUsername} to {channel.Mention}.", ephemeral: true);
    }

    [SlashCommand("list-x", "List tracked X accounts in this guild")]
    public async Task ListXAsync()
    {
        if (!TryValidateGuildContext(out _))
        {
            await RespondAsync("This command can only be used inside a guild.", ephemeral: true);
            return;
        }

        var guildId = unchecked((long)Context.Guild.Id);
        var feeds = await _db.TrackedFeeds
            .AsNoTracking()
            .Where(x => x.GuildId == guildId && x.IsActive)
            .OrderBy(x => x.XUsername)
            .ThenBy(x => x.ChannelId)
            .ToListAsync();

        if (feeds.Count == 0)
        {
            await RespondAsync("No X feeds are currently tracked in this guild.", ephemeral: true);
            return;
        }

        var lines = feeds
            .Select(x => $"- @{x.XUsername} -> <#{x.ChannelId}>")
            .Take(30);

        var embed = new EmbedBuilder()
            .WithTitle("Tracked X Feeds")
            .WithColor(new Color(29, 161, 242))
            .WithDescription(string.Join("\n", lines))
            .WithCurrentTimestamp()
            .Build();

        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("remove-x", "Remove tracked X feed mapping")]
    public async Task RemoveXAsync(string username, ITextChannel? channel = null)
    {
        if (!TryValidateGuildContext(out var guildUser))
        {
            await RespondAsync("This command can only be used inside a guild.", ephemeral: true);
            return;
        }

        if (!HasManagementPermission(guildUser))
        {
            await RespondAsync("You need Manage Server/Manage Channels permission to use this command.", ephemeral: true);
            return;
        }

        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrEmpty(normalizedUsername))
        {
            await RespondAsync("Invalid X username.", ephemeral: true);
            return;
        }

        var guildId = unchecked((long)Context.Guild.Id);
        var query = _db.TrackedFeeds.Where(x =>
            x.GuildId == guildId &&
            x.XUsername == normalizedUsername);

        if (channel is not null)
        {
            query = query.Where(x => x.ChannelId == unchecked((long)channel.Id));
        }

        var feeds = await query.ToListAsync();
        if (feeds.Count == 0)
        {
            await RespondAsync($"No tracked mapping found for @{normalizedUsername}.", ephemeral: true);
            return;
        }

        _db.TrackedFeeds.RemoveRange(feeds);
        await _db.SaveChangesAsync();

        await RespondAsync($"Removed {feeds.Count} mapping(s) for @{normalizedUsername}.", ephemeral: true);
    }

    private static bool HasManagementPermission(SocketGuildUser guildUser)
    {
        var permissions = guildUser.GuildPermissions;
        return permissions.Administrator || permissions.ManageGuild || permissions.ManageChannels;
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

    private string BuildRssUrl(string username)
    {
        var baseUrl = _rssBridgeOptions.Value.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/?action=display&bridge=TwitterBridge&context=By+username&u={Uri.EscapeDataString(username)}&format=Atom";
    }

    private async Task<bool> ValidateRssAsync(string rssUrl)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(rssUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RSS validation failed for URL {RssUrl}", rssUrl);
            return false;
        }
    }

    private static string NormalizeUsername(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var segment = uri.Segments.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(segment))
            {
                value = segment.Trim('/');
            }
        }

        if (value.StartsWith('@'))
        {
            value = value[1..];
        }

        value = value.ToLowerInvariant();
        value = Regex.Replace(value, "[^a-z0-9_]", string.Empty);

        if (value.Length is < 1 or > 15)
        {
            return string.Empty;
        }

        return value;
    }
}
