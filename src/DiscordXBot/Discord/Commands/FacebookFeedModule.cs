using System.Text.RegularExpressions;
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

public sealed class FacebookFeedModule(
    BotDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<RetryOptions> retryOptions,
    IOptionsMonitor<FeedProviderOptions> feedProviderOptions,
    FeedUrlResolver feedUrlResolver,
    ILogger<FacebookFeedModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDbContext _db = db;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptions<RetryOptions> _retryOptions = retryOptions;
    private readonly IOptionsMonitor<FeedProviderOptions> _feedProviderOptions = feedProviderOptions;
    private readonly FeedUrlResolver _feedUrlResolver = feedUrlResolver;
    private readonly ILogger<FacebookFeedModule> _logger = logger;

    [SlashCommand("add-fb", "Add a Facebook page feed to a Discord channel")]
    public async Task AddFacebookAsync(string pageOrId, ITextChannel channel, string? provider = null)
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

        var sourceKey = NormalizeFacebookSource(pageOrId);
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            await ReplyAsync("Invalid Facebook page/id.");
            return;
        }

        var selectedProvider = ParseProvider(provider) ?? _feedProviderOptions.CurrentValue.DefaultFacebookProvider;
        if (selectedProvider == FeedProvider.RssBridge || selectedProvider == FeedProvider.DirectRss)
        {
            await ReplyAsync("/add-fb currently supports RSSHub provider only. Use /add-link for direct FetchRSS URLs.");
            return;
        }

        if (!_feedUrlResolver.IsProviderEnabled(selectedProvider))
        {
            await ReplyAsync($"Provider {selectedProvider} is disabled by configuration.");
            return;
        }

        await EnsureDeferredAsync();

        string rssUrl;
        try
        {
            rssUrl = _feedUrlResolver.Resolve(FeedPlatform.Facebook, selectedProvider, sourceKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Facebook RSS URL for source {SourceKey}", sourceKey);
            await ReplyAsync("Unable to construct RSS URL from current provider settings.");
            return;
        }

        if (!await ValidateRssAsync(rssUrl))
        {
            await ReplyAsync($"Unable to validate Facebook feed for {sourceKey}.");
            return;
        }

        var guildId = unchecked((long)Context.Guild.Id);
        var channelId = unchecked((long)channel.Id);

        var existing = await _db.TrackedFeeds.FirstOrDefaultAsync(x =>
            x.GuildId == guildId &&
            x.ChannelId == channelId &&
            x.Platform == FeedPlatform.Facebook &&
            x.SourceKey == sourceKey);

        if (existing is not null)
        {
            await ReplyAsync($"{sourceKey} is already tracked in {channel.Mention}.");
            return;
        }

        _db.TrackedFeeds.Add(new TrackedFeed
        {
            GuildId = guildId,
            ChannelId = channelId,
            XUsername = BuildLegacyUsername(sourceKey, FeedPlatform.Facebook),
            SourceKey = sourceKey,
            Platform = FeedPlatform.Facebook,
            Provider = selectedProvider,
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
            _logger.LogInformation(ex, "Duplicate Facebook mapping prevented for source {SourceKey}", sourceKey);
            await ReplyAsync($"{sourceKey} is already tracked in {channel.Mention}.");
            return;
        }

        await ReplyAsync($"Added Facebook source {sourceKey} to {channel.Mention} via {selectedProvider}.");
    }

    [SlashCommand("list-fb", "List tracked Facebook feeds in this guild")]
    public async Task ListFacebookAsync()
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
            .Where(x => x.GuildId == guildId && x.IsActive && x.Platform == FeedPlatform.Facebook)
            .OrderBy(x => x.SourceKey)
            .ThenBy(x => x.ChannelId)
            .ToListAsync();

        if (feeds.Count == 0)
        {
            await ReplyAsync("No Facebook feeds are currently tracked in this guild.");
            return;
        }

        var lines = feeds
            .Select(x => $"- {x.SourceKey} -> <#{x.ChannelId}> ({x.Provider})")
            .Take(25)
            .ToList();

        if (feeds.Count > lines.Count)
        {
            lines.Add($"... and {feeds.Count - lines.Count} more");
        }

        var embed = new EmbedBuilder()
            .WithTitle("Tracked Facebook Feeds")
            .WithColor(new Color(66, 103, 178))
            .WithDescription(string.Join("\n", lines))
            .WithCurrentTimestamp()
            .Build();

        await ReplyAsync(string.Empty, embed);
    }

    [SlashCommand("remove-fb", "Remove tracked Facebook feed mapping")]
    public async Task RemoveFacebookAsync(string pageOrId, ITextChannel? channel = null)
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

        var sourceKey = NormalizeFacebookSource(pageOrId);
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            await ReplyAsync("Invalid Facebook page/id.");
            return;
        }

        await EnsureDeferredAsync();

        var guildId = unchecked((long)Context.Guild.Id);
        var query = _db.TrackedFeeds.Where(x =>
            x.GuildId == guildId &&
            x.Platform == FeedPlatform.Facebook &&
            x.SourceKey == sourceKey);

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
            await ReplyAsync($"No tracked mapping found for Facebook source {sourceKey}.");
            return;
        }

        _db.TrackedFeeds.RemoveRange(feeds);
        await _db.SaveChangesAsync();

        await ReplyAsync($"Removed {feeds.Count} mapping(s) for Facebook source {sourceKey}.");
    }

    private static FeedProvider? ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "rsshub" => FeedProvider.RssHub,
            "direct" => FeedProvider.DirectRss,
            "directrss" => FeedProvider.DirectRss,
            "fetchrss" => FeedProvider.DirectRss,
            "rssbridge" => FeedProvider.RssBridge,
            _ => null
        };
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
                    if (LooksLikeErrorPayload(body))
                    {
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

    private static bool LooksLikeErrorPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        return payload.Contains("Bridge returned error", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("404 Page Not Found", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("HttpException", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("Not Found", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFacebookSource(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var value = input.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?');
            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Equals("id", StringComparison.OrdinalIgnoreCase))
                    {
                        value = Uri.UnescapeDataString(parts[1]).Trim();
                        break;
                    }
                }
            }

            if (value == input.Trim())
            {
                var segments = uri.AbsolutePath
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => !x.Equals("posts", StringComparison.OrdinalIgnoreCase) &&
                                !x.Equals("videos", StringComparison.OrdinalIgnoreCase) &&
                                !x.Equals("photos", StringComparison.OrdinalIgnoreCase) &&
                                !x.Equals("profile.php", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (segments.Count > 0)
                {
                    value = segments[0];
                }
            }
        }

        if (value.StartsWith('@'))
        {
            value = value[1..];
        }

        value = Regex.Replace(value, "[^a-zA-Z0-9._-]", string.Empty);
        if (value.Length is < 2 or > 128)
        {
            return string.Empty;
        }

        return value;
    }

    private static string BuildLegacyUsername(string sourceKey, FeedPlatform platform)
    {
        var prefix = platform == FeedPlatform.Facebook ? "fb" : "x";
        var normalized = Regex.Replace(sourceKey.ToLowerInvariant(), "[^a-z0-9_]", string.Empty);
        if (normalized.Length == 0)
        {
            normalized = "source";
        }

        if (normalized.Length > 58)
        {
            normalized = normalized[..58];
        }

        return $"{prefix}_{normalized}";
    }
}
