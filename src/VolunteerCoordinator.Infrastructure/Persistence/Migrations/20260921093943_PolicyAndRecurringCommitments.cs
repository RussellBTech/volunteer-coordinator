using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PolicyAndRecurringCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SignupPolicy",
                table: "Shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SignupPolicy",
                table: "RecurringShiftSeriesRevisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RecurringCommitmentCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VolunteerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommitmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InvalidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringCommitmentCapabilities", x => x.Id);
                    table.CheckConstraint("CK_RecurringCommitmentCapabilities_TokenHash", "octet_length(\"TokenHash\") = 32");
                    table.ForeignKey(
                        name: "FK_RecurringCommitmentCapabilities_Volunteers_VolunteerId",
                        column: x => x.VolunteerId,
                        principalTable: "Volunteers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringCommitmentOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecurringOccurrenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringCommitmentOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringCommitmentOccurrences_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringCommitmentOccurrences_RecurringShiftOccurrences_Re~",
                        column: x => x.RecurringOccurrenceId,
                        principalTable: "RecurringShiftOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringCommitmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolunteerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleKind = table.Column<int>(type: "integer", nullable: false),
                    RolePosition = table.Column<int>(type: "integer", nullable: false),
                    EffectiveLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourcePolicy = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByCoordinatorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    RecurringCommitmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringCommitmentRequests", x => x.Id);
                    table.CheckConstraint("CK_RecurringCommitmentRequests_Range", "\"EndLocalDate\" >= \"EffectiveLocalDate\"");
                    table.ForeignKey(
                        name: "FK_RecurringCommitmentRequests_RecurringShiftSeriesRevisions_R~",
                        column: x => x.RevisionId,
                        principalTable: "RecurringShiftSeriesRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringCommitmentRequests_RecurringShiftSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "RecurringShiftSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringCommitmentRequests_Volunteers_VolunteerId",
                        column: x => x.VolunteerId,
                        principalTable: "Volunteers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringCommitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolunteerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleKind = table.Column<int>(type: "integer", nullable: false),
                    RolePosition = table.Column<int>(type: "integer", nullable: false),
                    EffectiveLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourcePolicy = table.Column<int>(type: "integer", nullable: false),
                    SourceRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawalEffectiveLocalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringCommitments", x => x.Id);
                    table.CheckConstraint("CK_RecurringCommitments_Range", "\"EndLocalDate\" >= \"EffectiveLocalDate\"");
                    table.ForeignKey(
                        name: "FK_RecurringCommitments_RecurringCommitmentRequests_SourceRequ~",
                        column: x => x.SourceRequestId,
                        principalTable: "RecurringCommitmentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringCommitments_RecurringShiftSeriesRevisions_Revision~",
                        column: x => x.RevisionId,
                        principalTable: "RecurringShiftSeriesRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringCommitments_RecurringShiftSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "RecurringShiftSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringCommitments_Volunteers_VolunteerId",
                        column: x => x.VolunteerId,
                        principalTable: "Volunteers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentCapabilities_CommitmentId",
                table: "RecurringCommitmentCapabilities",
                column: "CommitmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentCapabilities_RequestId",
                table: "RecurringCommitmentCapabilities",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentCapabilities_TokenHash",
                table: "RecurringCommitmentCapabilities",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentCapabilities_Volunteer_Commitment",
                table: "RecurringCommitmentCapabilities",
                columns: new[] { "VolunteerId", "CommitmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentOccurrences_CommitmentId_RecurringOccurr~",
                table: "RecurringCommitmentOccurrences",
                columns: new[] { "CommitmentId", "RecurringOccurrenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentOccurrences_Occurrence_State",
                table: "RecurringCommitmentOccurrences",
                columns: new[] { "RecurringOccurrenceId", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_RecurringCommitmentOccurrences_Assignment",
                table: "RecurringCommitmentOccurrences",
                column: "AssignmentId",
                unique: true,
                filter: "\"AssignmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentRequests_RecurringCommitmentId",
                table: "RecurringCommitmentRequests",
                column: "RecurringCommitmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentRequests_RevisionId",
                table: "RecurringCommitmentRequests",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitmentRequests_VolunteerId",
                table: "RecurringCommitmentRequests",
                column: "VolunteerId");

            migrationBuilder.CreateIndex(
                name: "UX_RecurringCommitmentRequests_Pending",
                table: "RecurringCommitmentRequests",
                columns: new[] { "SeriesId", "VolunteerId", "RoleKind", "RolePosition", "EffectiveLocalDate", "EndLocalDate" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitments_Overlap",
                table: "RecurringCommitments",
                columns: new[] { "SeriesId", "RoleKind", "RolePosition", "EffectiveLocalDate", "EndLocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitments_RevisionId",
                table: "RecurringCommitments",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitments_SourceRequestId",
                table: "RecurringCommitments",
                column: "SourceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitments_State_End",
                table: "RecurringCommitments",
                columns: new[] { "State", "EndLocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCommitments_VolunteerId",
                table: "RecurringCommitments",
                column: "VolunteerId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringCommitmentCapabilities_RecurringCommitmentRequests~",
                table: "RecurringCommitmentCapabilities",
                column: "RequestId",
                principalTable: "RecurringCommitmentRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringCommitmentCapabilities_RecurringCommitments_Commit~",
                table: "RecurringCommitmentCapabilities",
                column: "CommitmentId",
                principalTable: "RecurringCommitments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringCommitmentOccurrences_RecurringCommitments_Commitm~",
                table: "RecurringCommitmentOccurrences",
                column: "CommitmentId",
                principalTable: "RecurringCommitments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringCommitmentRequests_RecurringCommitments_RecurringC~",
                table: "RecurringCommitmentRequests",
                column: "RecurringCommitmentId",
                principalTable: "RecurringCommitments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
            migrationBuilder.Sql(
                """CREATE EXTENSION IF NOT EXISTS btree_gist;""");
            migrationBuilder.Sql(
                """
                ALTER TABLE "RecurringCommitments"
                ADD CONSTRAINT "EX_RecurringCommitments_ActiveRoleRange"
                EXCLUDE USING gist
                (
                    "SeriesId" WITH =,
                    "RoleKind" WITH =,
                    "RolePosition" WITH =,
                    daterange("EffectiveLocalDate", "EndLocalDate" + 1, '[]') WITH &&
                )
                WHERE ("State" IN (0, 1));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "RecurringCommitments" DROP CONSTRAINT IF EXISTS "EX_RecurringCommitments_ActiveRoleRange";""");
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringCommitments_RecurringCommitmentRequests_SourceRequ~",
                table: "RecurringCommitments");

            migrationBuilder.DropTable(
                name: "RecurringCommitmentCapabilities");

            migrationBuilder.DropTable(
                name: "RecurringCommitmentOccurrences");

            migrationBuilder.DropTable(
                name: "RecurringCommitmentRequests");

            migrationBuilder.DropTable(
                name: "RecurringCommitments");

            migrationBuilder.DropColumn(
                name: "SignupPolicy",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "SignupPolicy",
                table: "RecurringShiftSeriesRevisions");
        }
    }
}
