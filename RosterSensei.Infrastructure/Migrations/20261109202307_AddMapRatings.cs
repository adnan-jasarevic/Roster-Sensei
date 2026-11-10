using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMapRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MapRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MapName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<decimal>(type: "decimal(3,1)", precision: 3, scale: 1, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapRatings_TeamMembers_UserId_TeamId",
                        columns: x => new { x.UserId, x.TeamId },
                        principalTable: "TeamMembers",
                        principalColumns: new[] { "UserId", "TeamId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapRatings_TeamId",
                table: "MapRatings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MapRatings_UserId_TeamId_MapName_Side",
                table: "MapRatings",
                columns: new[] { "UserId", "TeamId", "MapName", "Side" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapRatings");
        }
    }
}
