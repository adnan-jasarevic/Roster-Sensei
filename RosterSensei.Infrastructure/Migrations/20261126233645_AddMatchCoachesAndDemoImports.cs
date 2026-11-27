using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchCoachesAndDemoImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoParserStatuses",
                columns: table => new
                {
                    ParserVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PausedForPatch = table.Column<int>(type: "int", nullable: false),
                    PausedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoParserStatuses", x => x.ParserVersion);
                });

            migrationBuilder.CreateTable(
                name: "MatchCoaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCoaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchCoaches_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchDemoImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    MapOrder = table.Column<int>(type: "int", nullable: false),
                    MapName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PatchVersion = table.Column<int>(type: "int", nullable: true),
                    ParserVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Rounds = table.Column<int>(type: "int", nullable: false),
                    ImportedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchDemoImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchDemoImports_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DemoPlayerStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchDemoImportId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Kills = table.Column<int>(type: "int", nullable: false),
                    Deaths = table.Column<int>(type: "int", nullable: false),
                    Assists = table.Column<int>(type: "int", nullable: false),
                    Damage = table.Column<int>(type: "int", nullable: false),
                    HeadshotKills = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoPlayerStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemoPlayerStats_MatchDemoImports_MatchDemoImportId",
                        column: x => x.MatchDemoImportId,
                        principalTable: "MatchDemoImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DemoPlayerStats_MatchDemoImportId_UserId",
                table: "DemoPlayerStats",
                columns: new[] { "MatchDemoImportId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchCoaches_MatchId_SteamId",
                table: "MatchCoaches",
                columns: new[] { "MatchId", "SteamId" },
                unique: true,
                filter: "[SteamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCoaches_MatchId_UserId",
                table: "MatchCoaches",
                columns: new[] { "MatchId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDemoImports_MatchId_MapOrder",
                table: "MatchDemoImports",
                columns: new[] { "MatchId", "MapOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchDemoImports_PatchVersion",
                table: "MatchDemoImports",
                column: "PatchVersion");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDemoImports_TeamId_Fingerprint",
                table: "MatchDemoImports",
                columns: new[] { "TeamId", "Fingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoParserStatuses");

            migrationBuilder.DropTable(
                name: "DemoPlayerStats");

            migrationBuilder.DropTable(
                name: "MatchCoaches");

            migrationBuilder.DropTable(
                name: "MatchDemoImports");
        }
    }
}
