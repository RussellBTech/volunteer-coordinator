using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringShiftSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecurringOccurrenceId",
                table: "Shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringShiftSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastGeneratedThroughLocalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringShiftSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringShiftSeriesRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    EffectiveLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VolunteerInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InternalCoordinatorNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecurrenceKind = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    WeeklyDays = table.Column<int>(type: "integer", nullable: false),
                    AnchorLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LocalStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BackupSlotCount = table.Column<int>(type: "integer", nullable: false),
                    HorizonWeeks = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AmbiguousTimeChoice = table.Column<int>(type: "integer", nullable: false),
                    CreatedByCoordinator = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringShiftSeriesRevisions", x => x.Id);
                    table.CheckConstraint("CK_RecurringShiftSeriesRevisions_BackupSlots", "\"BackupSlotCount\" BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_RecurringShiftSeriesRevisions_Duration", "\"DurationMinutes\" BETWEEN 1 AND 10080");
                    table.CheckConstraint("CK_RecurringShiftSeriesRevisions_Horizon", "\"HorizonWeeks\" BETWEEN 4 AND 26");
                    table.CheckConstraint("CK_RecurringShiftSeriesRevisions_Interval", "\"Interval\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_RecurringShiftSeriesRevisions_RecurringShiftSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "RecurringShiftSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringShiftOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsException = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedLocalStart = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResolutionActor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ResolutionAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolutionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringShiftOccurrences", x => x.Id);
                    table.CheckConstraint("CK_RecurringShiftOccurrences_State", "(\"Status\" = 0 AND \"ShiftId\" IS NOT NULL) OR (\"Status\" IN (1, 2) AND \"ShiftId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_RecurringShiftOccurrences_RecurringShiftSeriesRevisions_Rev~",
                        column: x => x.RevisionId,
                        principalTable: "RecurringShiftSeriesRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringShiftOccurrences_RecurringShiftSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "RecurringShiftSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Shifts_RecurringOccurrence",
                table: "Shifts",
                column: "RecurringOccurrenceId",
                unique: true,
                filter: "\"RecurringOccurrenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringShiftOccurrences_RevisionId",
                table: "RecurringShiftOccurrences",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringShiftOccurrences_SeriesId_LocalDate",
                table: "RecurringShiftOccurrences",
                columns: new[] { "SeriesId", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringShiftOccurrences_Status",
                table: "RecurringShiftOccurrences",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_RecurringShiftOccurrences_Shift",
                table: "RecurringShiftOccurrences",
                column: "ShiftId",
                unique: true,
                filter: "\"ShiftId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringShiftSeries_Active",
                table: "RecurringShiftSeries",
                columns: new[] { "IsActive", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringShiftSeriesRevisions_SeriesId_EffectiveLocalDate",
                table: "RecurringShiftSeriesRevisions",
                columns: new[] { "SeriesId", "EffectiveLocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringShiftSeriesRevisions_SeriesId_RevisionNumber",
                table: "RecurringShiftSeriesRevisions",
                columns: new[] { "SeriesId", "RevisionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_RecurringShiftOccurrences_RecurringOccurrenceId",
                table: "Shifts",
                column: "RecurringOccurrenceId",
                principalTable: "RecurringShiftOccurrences",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_RecurringShiftOccurrences_RecurringOccurrenceId",
                table: "Shifts");

            migrationBuilder.DropTable(
                name: "RecurringShiftOccurrences");

            migrationBuilder.DropTable(
                name: "RecurringShiftSeriesRevisions");

            migrationBuilder.DropTable(
                name: "RecurringShiftSeries");

            migrationBuilder.DropIndex(
                name: "UX_Shifts_RecurringOccurrence",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "RecurringOccurrenceId",
                table: "Shifts");
        }
    }
}
