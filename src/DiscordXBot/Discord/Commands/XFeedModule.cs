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
using Npgsql;

namespace DiscordXBot.Discord.Commands;

public sealed class XFeedModule(
    BotDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<RssBridgeOptions> rssBridgeOptions,
    IOptions<RetryOptions> retryOptions,
    ILogger<XFeedModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptions<RssBridgeOptions> _rssBridgeOptions = rssBridgeOptions;
    private readonly IOptions<RetryOptions> _retryOptions = retryOptions;
    private readonly ILogger<XFeedModule> _logger = logger;

    [SlashCommand("add-x", "Add an X account feed to a Discord channel")]
    public async Task AddXAsync(string username, ITextChannel channel)
    {
        _logger.LogInformation("add-x started. Raw username={Username}, channelId={ChannelId}", username, channel.Id);

        if (!TryValidateGuildContext(out var guildUser))
        {
            _logger.LogInformation("add-x aborted: not in guild context.");
            await ReplyAsync("This command can only be used inside a guild.");
            return;
        }

        if (!HasManagementPermission(guildUser))
        {
            _logger.LogInformation("add-x denied: missing manage permissions for user {UserId}", guildUser.Id);
            await ReplyAsync("You need Manage Server/Manage Channels permission to use this command.");
            return;
        }

        if (channel.GuildId != Context.Guild.Id)
        {
            _logger.LogInformation("add-x aborted: target channel not in guild.");
            await ReplyAsync("Target channel must belong to this guild.");
            return;
        }

        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrEmpty(normalizedUsername))
        {
            _logger.LogInformation("add-x aborted: invalid normalized username.");
            await ReplyAsync("Invalid X username.");
            return;
        }

        await EnsureDeferredAsync();

        var rssUrl = BuildRssUrl(normalizedUsername);
        if (!await ValidateRssAsync(rssUrl))
        {
            _logger.LogInformation("add-x validation failed for @{Username}", normalizedUsername);
            await ReplyAsync($"Unable to validate feed for @{normalizedUsername}. Check username or RSS-Bridge settings.");
            return;
        }

        var guildId = unchecked((long)Context.Guild.Id);
        var channelId = unchecked((long)channel.Id);

        var existing = await _db.TrackedFeeds.FirstOrDefaultAsync(x =>
            x.GuildId == guildId && x.ChannelId == channelId && x.XUsername == normalizedUsername);

        if (existing is not null)
        {
            _logger.LogInformation("add-x no-op: mapping already exists for @{Username} in channel {ChannelId}", normalizedUsername, channelId);
            await ReplyAsync($"@{normalizedUsername} is already tracked in {channel.Mention}.");
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

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation(
                ex,
                "Duplicate feed mapping prevented for guild {GuildId}, channel {ChannelId}, username {Username}",
                guildId,
                channelId,
                normalizedUsername);

            await ReplyAsync($"@{normalizedUsername} is already tracked in {channel.Mention}.");
            return;
        }

        await ReplyAsync($"Added @{normalizedUsername} to {channel.Mention}.");
        _logger.LogInformation("add-x completed: mapped @{Username} to channel {ChannelId}", normalizedUsername, channelId);
    }

    [SlashCommand("list-x", "List tracked X accounts in this guild")]
    public async Task ListXAsync()
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
            .Where(x => x.GuildId == guildId && x.IsActive)
            .OrderBy(x => x.XUsername)
            .ThenBy(x => x.ChannelId)
            .ToListAsync();

        if (feeds.Count == 0)
        {
            await ReplyAsync("No X feeds are currently tracked in this guild.");
            return;
        }

        var lines = feeds
            .Select(x => $"- @{x.XUsername} -> <#{x.ChannelId}>")
            .Take(25)
            .ToList();

        if (feeds.Count > lines.Count)
        {
            lines.Add($"... and {feeds.Count - lines.Count} more");
        }

        var embed = new EmbedBuilder()
            .WithTitle("Tracked X Feeds")
            .WithColor(new Color(29, 161, 242))
            .WithDescription(string.Join("\n", lines))
            .WithCurrentTimestamp()
            .Build();

        await ReplyAsync(string.Empty, embed);
    }

    [SlashCommand("remove-x", "Remove tracked X feed mapping")]
    public async Task RemoveXAsync(string username, ITextChannel? channel = null)
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

        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrEmpty(normalizedUsername))
        {
            await ReplyAsync("Invalid X username.");
            return;
        }

        await EnsureDeferredAsync();

        var guildId = unchecked((long)Context.Guild.Id);
        var query = _db.TrackedFeeds.Where(x =>
            x.GuildId == guildId &&
            x.XUsername == normalizedUsername);

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
            await ReplyAsync($"No tracked mapping found for @{normalizedUsername}.");
            return;
        }

        _db.TrackedFeeds.RemoveRange(feeds);
        await _db.SaveChangesAsync();

        await ReplyAsync($"Removed {feeds.Count} mapping(s) for @{normalizedUsername}.");
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
        var maxRetries = Math.Max(0, _retryOptions.Value.MaxRetries);
        var initialDelaySeconds = Math.Max(1, _retryOptions.Value.InitialDelaySeconds);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var client = _httpClientFactory.CreateClient();
                using var response = await client.GetAsync(rssUrl, HttpCompletionOption.ResponseContentRead, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cts.Token);
                    if (LooksLikeBridgeErrorPayload(body))
                    {
                        _logger.LogWarning("RSS validation returned bridge error payload for URL {RssUrl}", rssUrl);
                        return false;
                    }

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RSS validation failed for URL {RssUrl}", rssUrl);
                return false;
            }
        }

        return false;
    }

    private static bool LooksLikeBridgeErrorPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        return payload.Contains("Bridge returned error", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("404 Page Not Found", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("RSS-Bridge tried to fetch a page", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("HttpException", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: "23505" };
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
