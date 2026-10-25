using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalAnalysisApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiCredentialId",
                table: "Videos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedVia",
                table: "Videos",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "web");

            migrationBuilder.CreateTable(
                name: "ApiCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastUsedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiCredentials_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiRequestLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CredentialId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    CredentialPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ResponseBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    RemoteAddressHash = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRequestLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiRequestLogs_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Videos_OwnerId_CreatedVia_CreatedAt",
                table: "Videos",
                columns: new[] { "OwnerId", "CreatedVia", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiCredentials_OwnerId_CreatedAt",
                table: "ApiCredentials",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiCredentials_Prefix",
                table: "ApiCredentials",
                column: "Prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestLogs_CredentialId_CreatedAt",
                table: "ApiRequestLogs",
                columns: new[] { "CredentialId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestLogs_OwnerId_CreatedAt",
                table: "ApiRequestLogs",
                columns: new[] { "OwnerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiCredentials");

            migrationBuilder.DropTable(
                name: "ApiRequestLogs");

            migrationBuilder.DropIndex(
                name: "IX_Videos_OwnerId_CreatedVia_CreatedAt",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ApiCredentialId",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "CreatedVia",
                table: "Videos");
        }
    }
}
