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
    IOptionsMonitor<ApifyOptions> apifyOptions,
    FeedUrlResolver feedUrlResolver,
    ILogger<FacebookFeedModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDbContext _db = db;
    private readonly IOptionsMonitor<ApifyOptions> _apifyOptions = apifyOptions;
    private readonly FeedUrlResolver _feedUrlResolver = feedUrlResolver;
    private readonly ILogger<FacebookFeedModule> _logger = logger;

    [SlashCommand("add-fb", "Add a Facebook feed (fanpage/profile) to a Discord channel via Apify")]
    public async Task AddFacebookAsync(
        string fanpageOrId,
        ITextChannel channel,
        string sourceType = "fanpage")
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

        var sourceKey = NormalizeFanpageSource(fanpageOrId);
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            await ReplyAsync("Invalid Facebook source handle/id.");
            return;
        }

        if (!TryParseSourceType(sourceType, out var selectedSourceType))
        {
            await ReplyAsync("Unsupported source type. Use: fanpage or profile.");
            return;
        }

        if (selectedSourceType == FacebookSourceType.Profile && !IsNumericProfileSource(sourceKey))
        {
            await ReplyAsync("Profile source type currently requires a numeric Facebook ID.");
            return;
        }

        var apifyValidation = ValidateApifyConfiguration(selectedSourceType);
        if (!apifyValidation.IsValid)
        {
            await ReplyAsync(apifyValidation.Message);
            return;
        }

        await EnsureDeferredAsync();

        string facebookUrl;
        try
        {
            facebookUrl = _feedUrlResolver.Resolve(
                FeedPlatform.Facebook,
                FeedProvider.Apify,
                sourceKey,
                selectedSourceType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve Facebook URL for {SourceType} source {SourceKey}",
                GetSourceTypeLabel(selectedSourceType),
                sourceKey);
            await ReplyAsync("Unable to construct Facebook URL from current provider settings.");
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
            SourceType = selectedSourceType,
            Provider = FeedProvider.Apify,
            RssUrl = facebookUrl,
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
            _logger.LogInformation(ex, "Duplicate Facebook feed mapping prevented for source {SourceKey}", sourceKey);
            await ReplyAsync($"{sourceKey} is already tracked in {channel.Mention}.");
            return;
        }

        await ReplyAsync($"Added Facebook {GetSourceTypeLabel(selectedSourceType)} {sourceKey} to {channel.Mention} via Apify.");
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
            .Where(x =>
                x.GuildId == guildId &&
                x.IsActive &&
                x.Platform == FeedPlatform.Facebook &&
                x.Provider == FeedProvider.Apify)
            .OrderBy(x => x.SourceKey)
            .ThenBy(x => x.ChannelId)
            .ToListAsync();

        if (feeds.Count == 0)
        {
            await ReplyAsync("No Facebook feeds are currently tracked in this guild.");
            return;
        }

        var lines = feeds
            .Select(x => $"- [{GetSourceTypeLabel(x.SourceType)}] {x.SourceKey} -> <#{x.ChannelId}> ({x.Provider})")
            .Take(25)
            .ToList();

        if (feeds.Count > lines.Count)
        {
            lines.Add($"... and {feeds.Count - lines.Count} more");
        }

        var embed = new EmbedBuilder()
            .WithTitle("Tracked Facebook Sources")
            .WithColor(new Color(66, 103, 178))
            .WithDescription(string.Join("\n", lines))
            .WithCurrentTimestamp()
            .Build();

        await ReplyAsync(string.Empty, embed);
    }

    [SlashCommand("remove-fb", "Remove tracked Facebook feed mapping")]
    public async Task RemoveFacebookAsync(string fanpageOrId, ITextChannel? channel = null)
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

        var sourceKey = NormalizeFanpageSource(fanpageOrId);
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            await ReplyAsync("Invalid Facebook source handle/id.");
            return;
        }

        await EnsureDeferredAsync();

        var guildId = unchecked((long)Context.Guild.Id);
        var query = _db.TrackedFeeds.Where(x =>
            x.GuildId == guildId &&
            x.Platform == FeedPlatform.Facebook &&
            x.Provider == FeedProvider.Apify &&
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

    private ApifyConfigValidation ValidateApifyConfiguration(FacebookSourceType sourceType)
    {
        var options = _apifyOptions.CurrentValue;
        if (!options.Enabled)
        {
            return ApifyConfigValidation.Invalid("Apify is disabled. Set APIFY__ENABLED=true before using /add-fb. Use /add-link for direct RSS URLs.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            return ApifyConfigValidation.Invalid("Apify API token is missing. Set APIFY__APITOKEN before using /add-fb.");
        }

        if (string.IsNullOrWhiteSpace(options.ActorId))
        {
            return ApifyConfigValidation.Invalid("Apify actor id is missing. Set APIFY__ACTORID before using /add-fb.");
        }

        if (sourceType == FacebookSourceType.Fanpage && !options.EnableForFanpage)
        {
            return ApifyConfigValidation.Invalid("Apify fanpage ingestion is disabled. Set APIFY__ENABLEFORFANPAGE=true or use /add-link.");
        }

        if (sourceType == FacebookSourceType.Profile && !options.EnableForProfile)
        {
            return ApifyConfigValidation.Invalid("Apify profile ingestion is disabled. Set APIFY__ENABLEFORPROFILE=true or use /add-link.");
        }

        return ApifyConfigValidation.Valid();
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

    private static bool TryParseSourceType(string value, out FacebookSourceType sourceType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            sourceType = FacebookSourceType.Fanpage;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "fanpage":
            case "page":
                sourceType = FacebookSourceType.Fanpage;
                return true;
            case "profile":
            case "personal":
            case "personalprofile":
                sourceType = FacebookSourceType.Profile;
                return true;
            default:
                sourceType = FacebookSourceType.Fanpage;
                return false;
        }
    }

    private static bool IsNumericProfileSource(string sourceKey)
    {
        return sourceKey.All(char.IsDigit);
    }

    private static string GetSourceTypeLabel(FacebookSourceType sourceType)
    {
        return sourceType == FacebookSourceType.Profile ? "profile" : "fanpage";
    }

    private static string NormalizeFanpageSource(string input)
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

    private sealed record ApifyConfigValidation(bool IsValid, string Message)
    {
        public static ApifyConfigValidation Valid()
        {
            return new ApifyConfigValidation(true, string.Empty);
        }

        public static ApifyConfigValidation Invalid(string message)
        {
            return new ApifyConfigValidation(false, message);
        }
    }
}
