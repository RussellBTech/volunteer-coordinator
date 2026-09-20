using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PresentCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VolunteerInstructions",
                table: "Shifts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSettings", x => x.Id);
                    table.CheckConstraint("CK_GroupSettings_Singleton", "\"Id\" = '00000000-0000-0000-0000-000000000001'");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "VolunteerInstructions",
                table: "Shifts");
        }
    }
}
