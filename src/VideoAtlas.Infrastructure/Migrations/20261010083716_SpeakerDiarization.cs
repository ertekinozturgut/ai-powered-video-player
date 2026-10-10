using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpeakerDiarization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpectedSpeakerCount",
                table: "Videos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SpeakerDiarizationEnabled",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SpeakerNames",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedSpeakerCount",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "SpeakerDiarizationEnabled",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "SpeakerNames",
                table: "Videos");
        }
    }
}
