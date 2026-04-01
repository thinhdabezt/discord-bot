using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordXBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookSourceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                table: "tracked_feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "tracked_feeds");
        }
    }
}
