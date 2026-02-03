using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSMS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMasterScheduleAndBackupFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MasterScheduleEntries");

            migrationBuilder.DropColumn(
                name: "IsBackup",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "MonthPublishedAt",
                table: "Shifts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBackup",
                table: "Volunteers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MonthPublishedAt",
                table: "Shifts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MasterScheduleEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DefaultVolunteerId = table.Column<int>(type: "integer", nullable: true),
                    TimeSlotId = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterScheduleEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MasterScheduleEntries_TimeSlots_TimeSlotId",
                        column: x => x.TimeSlotId,
                        principalTable: "TimeSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MasterScheduleEntries_Volunteers_DefaultVolunteerId",
                        column: x => x.DefaultVolunteerId,
                        principalTable: "Volunteers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MasterScheduleEntries_DayOfWeek_TimeSlotId",
                table: "MasterScheduleEntries",
                columns: new[] { "DayOfWeek", "TimeSlotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterScheduleEntries_DefaultVolunteerId",
                table: "MasterScheduleEntries",
                column: "DefaultVolunteerId");

            migrationBuilder.CreateIndex(
                name: "IX_MasterScheduleEntries_TimeSlotId",
                table: "MasterScheduleEntries",
                column: "TimeSlotId");
        }
    }
}
