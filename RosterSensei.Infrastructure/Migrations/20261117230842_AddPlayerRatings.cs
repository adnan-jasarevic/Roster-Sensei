using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Rating",
                table: "MatchParticipation",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rating",
                table: "MatchParticipation");
        }
    }
}
