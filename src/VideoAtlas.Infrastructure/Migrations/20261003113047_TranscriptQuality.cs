using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TranscriptQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisInstructions",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OriginalSegments",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "ReviewVersion",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Terms",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "TranscriptHistory",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "TranscriptIssues",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "TranscriptVersion",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
            // Existing transcripts become the preserved baseline; no user content is discarded.
            migrationBuilder.Sql("UPDATE Videos SET OriginalSegments = Segments, TranscriptVersion = CASE WHEN Segments <> '[]' THEN 1 ELSE 0 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnalysisInstructions",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "OriginalSegments",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ReviewVersion",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "Terms",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "TranscriptHistory",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "TranscriptIssues",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "TranscriptVersion",
                table: "Videos");
        }
    }
}
