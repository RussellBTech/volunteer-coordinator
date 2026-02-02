using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSMS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OneShiftPerTimeslot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Delete duplicate shifts (keep the one with lowest Id per Date+TimeSlotId)
            migrationBuilder.Sql(@"
                DELETE FROM ""Shifts""
                WHERE ""Id"" NOT IN (
                    SELECT MIN(""Id"")
                    FROM ""Shifts""
                    GROUP BY ""Date"", ""TimeSlotId""
                )
            ");

            // Step 2: Delete duplicate master schedule entries (keep the one with lowest Id per DayOfWeek+TimeSlotId)
            migrationBuilder.Sql(@"
                DELETE FROM ""MasterScheduleEntries""
                WHERE ""Id"" NOT IN (
                    SELECT MIN(""Id"")
                    FROM ""MasterScheduleEntries""
                    GROUP BY ""DayOfWeek"", ""TimeSlotId""
                )
            ");

            // Step 3: Drop old unique constraint on Shifts
            migrationBuilder.DropIndex(
                name: "IX_Shifts_Date_TimeSlotId_Role",
                table: "Shifts");

            // Step 4: Create new unique constraint on Shifts (Date, TimeSlotId only)
            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Date_TimeSlotId",
                table: "Shifts",
                columns: new[] { "Date", "TimeSlotId" },
                unique: true);

            // Step 5: Drop old unique constraint on MasterScheduleEntries
            migrationBuilder.DropIndex(
                name: "IX_MasterScheduleEntries_DayOfWeek_TimeSlotId_Role",
                table: "MasterScheduleEntries");

            // Step 6: Drop Role column from MasterScheduleEntries
            migrationBuilder.DropColumn(
                name: "Role",
                table: "MasterScheduleEntries");

            // Step 7: Create new unique constraint on MasterScheduleEntries (DayOfWeek, TimeSlotId only)
            migrationBuilder.CreateIndex(
                name: "IX_MasterScheduleEntries_DayOfWeek_TimeSlotId",
                table: "MasterScheduleEntries",
                columns: new[] { "DayOfWeek", "TimeSlotId" },
                unique: true);

            // Step 8: Update Saturday Morning timeslot from 10am to 9am
            migrationBuilder.UpdateData(
                table: "TimeSlots",
                keyColumn: "Id",
                keyValue: 4,
                column: "StartTime",
                value: new TimeOnly(9, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert Saturday Morning timeslot to 10am
            migrationBuilder.UpdateData(
                table: "TimeSlots",
                keyColumn: "Id",
                keyValue: 4,
                column: "StartTime",
                value: new TimeOnly(10, 0, 0));

            // Drop new unique constraint on MasterScheduleEntries
            migrationBuilder.DropIndex(
                name: "IX_MasterScheduleEntries_DayOfWeek_TimeSlotId",
                table: "MasterScheduleEntries");

            // Re-add Role column to MasterScheduleEntries with default value 0 (InPerson)
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "MasterScheduleEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Re-create old unique constraint on MasterScheduleEntries
            migrationBuilder.CreateIndex(
                name: "IX_MasterScheduleEntries_DayOfWeek_TimeSlotId_Role",
                table: "MasterScheduleEntries",
                columns: new[] { "DayOfWeek", "TimeSlotId", "Role" },
                unique: true);

            // Drop new unique constraint on Shifts
            migrationBuilder.DropIndex(
                name: "IX_Shifts_Date_TimeSlotId",
                table: "Shifts");

            // Re-create old unique constraint on Shifts
            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Date_TimeSlotId_Role",
                table: "Shifts",
                columns: new[] { "Date", "TimeSlotId", "Role" },
                unique: true);
        }
    }
}
