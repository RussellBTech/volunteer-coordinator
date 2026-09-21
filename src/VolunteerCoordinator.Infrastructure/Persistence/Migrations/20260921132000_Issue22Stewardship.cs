using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Issue22Stewardship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Volunteers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ShiftId",
                table: "AuditEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VolunteerId",
                table: "AuditEntries",
                type: "uuid",
                nullable: true);
            migrationBuilder.Sql(
                """UPDATE "Volunteers" SET "NormalizedName" = UPPER(BTRIM("Name"));""");
            migrationBuilder.Sql(
                """
                UPDATE "AuditEntries"
                SET "ShiftId" = ("DetailJson"->>'ShiftId')::uuid
                WHERE "ShiftId" IS NULL
                  AND "DetailJson" ? 'ShiftId'
                  AND jsonb_typeof("DetailJson"->'ShiftId') = 'string'
                  AND ("DetailJson"->>'ShiftId') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$'
                  AND EXISTS (SELECT 1 FROM "Shifts" s WHERE s."Id" = ("DetailJson"->>'ShiftId')::uuid);
                UPDATE "AuditEntries" a
                SET "ShiftId" = s."Id"
                FROM "Shifts" s
                WHERE a."ShiftId" IS NULL
                  AND a."EntityKind" = 'Shift'
                  AND a."EntityId" = s."Id";
                UPDATE "AuditEntries" a
                SET "ShiftId" = s."Id"
                FROM "Assignments" assignment
                JOIN "Shifts" s ON s."Id" = assignment."ShiftId"
                WHERE a."ShiftId" IS NULL
                  AND a."EntityKind" = 'Assignment'
                  AND a."EntityId" = assignment."Id";
                UPDATE "AuditEntries" a
                SET "ShiftId" = s."Id"
                FROM "ShiftRequests" request
                JOIN "ShiftSlots" slot ON slot."Id" = request."ShiftSlotId"
                JOIN "Shifts" s ON s."Id" = slot."ShiftId"
                WHERE a."ShiftId" IS NULL
                  AND a."EntityKind" = 'ShiftRequest'
                  AND a."EntityId" = request."Id";
                UPDATE "AuditEntries"
                SET "VolunteerId" = ("DetailJson"->>'VolunteerId')::uuid
                WHERE "VolunteerId" IS NULL
                  AND "DetailJson" ? 'VolunteerId'
                  AND jsonb_typeof("DetailJson"->'VolunteerId') = 'string'
                  AND ("DetailJson"->>'VolunteerId') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$'
                  AND EXISTS (SELECT 1 FROM "Volunteers" v WHERE v."Id" = ("DetailJson"->>'VolunteerId')::uuid);
                UPDATE "AuditEntries" a
                SET "VolunteerId" = v."Id"
                FROM "Volunteers" v
                WHERE a."VolunteerId" IS NULL
                  AND a."EntityKind" = 'Volunteer'
                  AND a."EntityId" = v."Id";
                UPDATE "AuditEntries" a
                SET "VolunteerId" = v."Id", "ShiftId" = COALESCE(a."ShiftId", s."Id")
                FROM "Assignments" assignment
                JOIN "Volunteers" v ON v."Id" = assignment."VolunteerId"
                JOIN "Shifts" s ON s."Id" = assignment."ShiftId"
                WHERE a."EntityKind" = 'Assignment'
                  AND a."EntityId" = assignment."Id";
                UPDATE "AuditEntries" a
                SET "VolunteerId" = v."Id", "ShiftId" = COALESCE(a."ShiftId", s."Id")
                FROM "ShiftRequests" request
                JOIN "Volunteers" v ON v."Id" = request."VolunteerId"
                JOIN "ShiftSlots" slot ON slot."Id" = request."ShiftSlotId"
                JOIN "Shifts" s ON s."Id" = slot."ShiftId"
                WHERE a."EntityKind" = 'ShiftRequest'
                  AND a."EntityId" = request."Id";
                """);
            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_NormalizedName",
                table: "Volunteers",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Action_OccurredAtUtc_Id",
                table: "AuditEntries",
                columns: new[] { "Action", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Actor_OccurredAtUtc_Id",
                table: "AuditEntries",
                columns: new[] { "Actor", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc_Id",
                table: "AuditEntries",
                columns: new[] { "OccurredAtUtc", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Shift_OccurredAtUtc_Id",
                table: "AuditEntries",
                columns: new[] { "ShiftId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true },
                filter: "\"ShiftId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Volunteer_OccurredAtUtc_Id",
                table: "AuditEntries",
                columns: new[] { "VolunteerId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true },
                filter: "\"VolunteerId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Action_OccurredAtUtc_Id",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Actor_OccurredAtUtc_Id",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OccurredAtUtc_Id",
                table: "AuditEntries");


            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Shift_OccurredAtUtc_Id",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Volunteer_OccurredAtUtc_Id",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_Volunteers_NormalizedName",
                table: "Volunteers");
            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "VolunteerId",
                table: "AuditEntries");
        }
    }
}
