using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerMatchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchStat");

            migrationBuilder.AddColumn<decimal>(
                name: "Adr",
                table: "MatchParticipation",
                type: "decimal(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Assists",
                table: "MatchParticipation",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Deaths",
                table: "MatchParticipation",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeadshotPercentage",
                table: "MatchParticipation",
                type: "decimal(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kills",
                table: "MatchParticipation",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Adr",
                table: "MatchParticipation");

            migrationBuilder.DropColumn(
                name: "Assists",
                table: "MatchParticipation");

            migrationBuilder.DropColumn(
                name: "Deaths",
                table: "MatchParticipation");

            migrationBuilder.DropColumn(
                name: "HeadshotPercentage",
                table: "MatchParticipation");

            migrationBuilder.DropColumn(
                name: "Kills",
                table: "MatchParticipation");

            migrationBuilder.CreateTable(
                name: "MatchStat",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchId = table.Column<int>(type: "int", nullable: false),
                    StatType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Value = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchStat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchStat_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchStat_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchStat_MatchId",
                table: "MatchStat",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchStat_UserId",
                table: "MatchStat",
                column: "UserId");
        }
    }
}
