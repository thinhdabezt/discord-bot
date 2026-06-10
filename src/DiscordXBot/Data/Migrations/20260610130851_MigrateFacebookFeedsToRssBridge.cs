using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordXBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class MigrateFacebookFeedsToRssBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tracked_feeds
                SET
                    "Provider" = 0,
                    "UpdatedAtUtc" = NOW() AT TIME ZONE 'UTC'
                WHERE "Platform" = 1
                  AND "Provider" = 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tracked_feeds
                SET
                    "Provider" = 2,
                    "UpdatedAtUtc" = NOW() AT TIME ZONE 'UTC'
                WHERE "Platform" = 1
                  AND "Provider" = 0;
                """);
        }
    }
}
