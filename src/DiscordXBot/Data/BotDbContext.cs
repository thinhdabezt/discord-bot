using DiscordXBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordXBot.Data;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<TrackedFeed> TrackedFeeds => Set<TrackedFeed>();
    public DbSet<ProcessedTweet> ProcessedTweets => Set<ProcessedTweet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackedFeed>(entity =>
        {
            entity.ToTable("tracked_feeds");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.XUsername)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(x => x.SourceKey)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(x => x.Platform)
                .HasConversion<int>();

            entity.Property(x => x.SourceType)
                .HasConversion<int>();

            entity.Property(x => x.Provider)
                .HasConversion<int>();

            entity.Property(x => x.RssUrl)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.Property(x => x.UpdatedAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.HasIndex(x => new { x.GuildId, x.ChannelId, x.Platform, x.SourceKey })
                .IsUnique();

            entity.HasIndex(x => new { x.GuildId, x.Platform, x.SourceKey });
        });

        modelBuilder.Entity<ProcessedTweet>(entity =>
        {
            entity.ToTable("processed_tweets");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.XUsername)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(x => x.TweetId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(x => x.TweetUrl)
                .HasMaxLength(2048);

            entity.Property(x => x.ProcessedAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.HasIndex(x => new { x.TrackedFeedId, x.TweetId })
                .IsUnique();

            entity.HasIndex(x => new { x.GuildId, x.XUsername, x.ProcessedAtUtc });

            entity.HasOne(x => x.TrackedFeed)
                .WithMany(x => x.ProcessedTweets)
                .HasForeignKey(x => x.TrackedFeedId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
