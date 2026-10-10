using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoAtlas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FullStaticContentCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UiContentDefinitions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultText = table.Column<string>(type: "TEXT", nullable: false),
                    Context = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UiContentDefinitions", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UiContentDefinitions");
        }
    }
}
