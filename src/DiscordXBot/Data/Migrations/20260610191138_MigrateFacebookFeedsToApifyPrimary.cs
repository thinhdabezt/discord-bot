using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordXBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class MigrateFacebookFeedsToApifyPrimary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tracked_feeds
                SET
                    "Provider" = 3,
                    "RssUrl" = CASE
                        WHEN "SourceKey" IS NULL OR btrim("SourceKey") = '' THEN "RssUrl"
                        WHEN "SourceKey" ILIKE 'http://%' OR "SourceKey" ILIKE 'https://%' THEN "SourceKey"
                        ELSE 'https://www.facebook.com/' || "SourceKey" || '/'
                    END,
                    "UpdatedAtUtc" = NOW() AT TIME ZONE 'UTC'
                WHERE "Platform" = 1
                  AND "Provider" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tracked_feeds
                SET
                    "Provider" = 0,
                    "UpdatedAtUtc" = NOW() AT TIME ZONE 'UTC'
                WHERE "Platform" = 1
                  AND "Provider" = 3;
                """);
        }
    }
}
