using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSMS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupVolunteerSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Backup1VolunteerId",
                table: "Shifts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Backup2VolunteerId",
                table: "Shifts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Backup1VolunteerId",
                table: "Shifts",
                column: "Backup1VolunteerId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Backup2VolunteerId",
                table: "Shifts",
                column: "Backup2VolunteerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Volunteers_Backup1VolunteerId",
                table: "Shifts",
                column: "Backup1VolunteerId",
                principalTable: "Volunteers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Volunteers_Backup2VolunteerId",
                table: "Shifts",
                column: "Backup2VolunteerId",
                principalTable: "Volunteers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Volunteers_Backup1VolunteerId",
                table: "Shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Volunteers_Backup2VolunteerId",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_Backup1VolunteerId",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_Backup2VolunteerId",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "Backup1VolunteerId",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "Backup2VolunteerId",
                table: "Shifts");
        }
    }
}
