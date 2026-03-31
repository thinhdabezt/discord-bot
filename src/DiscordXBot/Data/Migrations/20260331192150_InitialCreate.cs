using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DiscordXBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tracked_feeds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    XUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RssUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracked_feeds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "processed_tweets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrackedFeedId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    XUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TweetId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TweetUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_tweets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processed_tweets_tracked_feeds_TrackedFeedId",
                        column: x => x.TrackedFeedId,
                        principalTable: "tracked_feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_processed_tweets_GuildId_XUsername_ProcessedAtUtc",
                table: "processed_tweets",
                columns: new[] { "GuildId", "XUsername", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_processed_tweets_TrackedFeedId_TweetId",
                table: "processed_tweets",
                columns: new[] { "TrackedFeedId", "TweetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracked_feeds_GuildId_XUsername_ChannelId",
                table: "tracked_feeds",
                columns: new[] { "GuildId", "XUsername", "ChannelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_tweets");

            migrationBuilder.DropTable(
                name: "tracked_feeds");
        }
    }
}
