using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioMediaKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaKind",
                table: "Videos",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "video");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaKind",
                table: "Videos");
        }
    }
}
