using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Matches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // The Live status (1) was removed. A match left in it goes back to Scheduled, so it shows as
            // upcoming or "Needs result" and can get its result like any other.
            migrationBuilder.Sql("UPDATE [Matches] SET [Status] = 0 WHERE [Status] = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Matches");
        }
    }
}
