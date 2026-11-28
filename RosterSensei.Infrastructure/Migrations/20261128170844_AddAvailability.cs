using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RosterSensei.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAvailabilities",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WeeklySlots = table.Column<string>(type: "char(336)", unicode: false, fixedLength: true, maxLength: 336, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAvailabilities", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserAvailabilities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AvailabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Slots = table.Column<string>(type: "char(48)", unicode: false, fixedLength: true, maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvailabilityOverrides_UserAvailabilities_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAvailabilities",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityOverrides_UserId_Date",
                table: "AvailabilityOverrides",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvailabilityOverrides");

            migrationBuilder.DropTable(
                name: "UserAvailabilities");
        }
    }
}
