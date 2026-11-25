using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StoreSteamProfilesAndSignIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SteamAvatarUrl",
                table: "AspNetUsers",
                type: "varchar(512)",
                unicode: false,
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SteamPersonaName",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SteamProfileRefreshDueAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SteamSignInStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ReturnTo = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SteamSignInStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SteamSignInStates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SteamProfileRefreshDueAt",
                table: "AspNetUsers",
                column: "SteamProfileRefreshDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_SteamSignInStates_ExpiresAt",
                table: "SteamSignInStates",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SteamSignInStates_UserId",
                table: "SteamSignInStates",
                column: "UserId");

            // The master tester tools' made-up players have random Steam IDs that may belong to real
            // strangers, so the background refresh must never look them up.
            migrationBuilder.Sql(
                "UPDATE [AspNetUsers] SET [SteamProfileRefreshDueAt] = '9999-12-31T00:00:00' " +
                "WHERE [Email] LIKE 'seed-%@rostersensei.test' AND [SteamId] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SteamSignInStates");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SteamProfileRefreshDueAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SteamAvatarUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SteamPersonaName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SteamProfileRefreshDueAt",
                table: "AspNetUsers");
        }
    }
}
