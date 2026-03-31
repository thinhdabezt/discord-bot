using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordXBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedPlatformProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tracked_feeds_GuildId_XUsername_ChannelId",
                table: "tracked_feeds");

            migrationBuilder.AddColumn<int>(
                name: "Platform",
                table: "tracked_feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "tracked_feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "tracked_feeds",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE tracked_feeds SET \"SourceKey\" = \"XUsername\" WHERE \"SourceKey\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_feeds_GuildId_ChannelId_Platform_SourceKey",
                table: "tracked_feeds",
                columns: new[] { "GuildId", "ChannelId", "Platform", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracked_feeds_GuildId_Platform_SourceKey",
                table: "tracked_feeds",
                columns: new[] { "GuildId", "Platform", "SourceKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tracked_feeds_GuildId_ChannelId_Platform_SourceKey",
                table: "tracked_feeds");

            migrationBuilder.DropIndex(
                name: "IX_tracked_feeds_GuildId_Platform_SourceKey",
                table: "tracked_feeds");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "tracked_feeds");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "tracked_feeds");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "tracked_feeds");

            migrationBuilder.CreateIndex(
                name: "IX_tracked_feeds_GuildId_XUsername_ChannelId",
                table: "tracked_feeds",
                columns: new[] { "GuildId", "XUsername", "ChannelId" },
                unique: true);
        }
    }
}
