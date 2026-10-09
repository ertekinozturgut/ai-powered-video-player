using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VideoOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelRequested",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "DeleteAfter",
                table: "Videos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FullReviewEnabled",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingSteps",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "QualityCursor",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ShareChapters",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareChat",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareRequiresLogin",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "ShareStartAt",
                table: "Videos",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "ShareTheme",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "orbital");

            migrationBuilder.AddColumn<bool>(
                name: "ShareTranscript",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ShareViewers",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "SilenceChecked",
                table: "Videos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptChunks",
                table: "Videos",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "SecondarySpeech",
                table: "ProviderSelections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", nullable: false),
                    Capability = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    TokensReported = table.Column<bool>(type: "INTEGER", nullable: false),
                    AudioSeconds = table.Column<double>(type: "REAL", nullable: true),
                    EstimatedUsd = table.Column<double>(type: "REAL", nullable: true),
                    ReportedUsd = table.Column<double>(type: "REAL", nullable: true),
                    Milliseconds = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiUsage_Videos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "Videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsage_OwnerId_CreatedAt",
                table: "AiUsage",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsage_VideoId",
                table: "AiUsage",
                column: "VideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsage");

            migrationBuilder.DropColumn(
                name: "CancelRequested",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "DeleteAfter",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "FullReviewEnabled",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ProcessingSteps",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "QualityCursor",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ShareChapters",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ShareChat",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ShareRequiresLogin",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ShareStartAt",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ShareTheme",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ShareTranscript",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ShareViewers",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "SilenceChecked",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "TranscriptChunks",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "SecondarySpeech",
                table: "ProviderSelections");
        }
    }
}
