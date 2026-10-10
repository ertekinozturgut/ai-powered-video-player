using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserProfileDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "UserLanguagePreferences",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "UserLanguagePreferences");
        }
    }
}
