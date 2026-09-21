using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Issue21CorrectionGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringCommitmentRequests_Range",
                table: "RecurringCommitmentRequests");
            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringCommitmentRequests_Range",
                table: "RecurringCommitmentRequests",
                sql: "\"EndLocalDate\" >= \"EffectiveLocalDate\"");
            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringCommitmentRequests_Horizon",
                table: "RecurringCommitmentRequests",
                sql: "(\"EndLocalDate\" - \"EffectiveLocalDate\" + 1) BETWEEN 28 AND 182");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringCommitments_Range",
                table: "RecurringCommitments");
            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringCommitments_Range",
                table: "RecurringCommitments",
                sql: "\"EndLocalDate\" >= \"EffectiveLocalDate\"");
            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringCommitments_Horizon",
                table: "RecurringCommitments",
                sql: "(\"EndLocalDate\" - \"EffectiveLocalDate\" + 1) BETWEEN 28 AND 182");

            migrationBuilder.Sql(
                """ALTER TABLE "RecurringCommitments" DROP CONSTRAINT IF EXISTS "EX_RecurringCommitments_ActiveRoleRange";""");
            migrationBuilder.Sql(
                """
                ALTER TABLE "RecurringCommitments"
                ADD CONSTRAINT "EX_RecurringCommitments_ActiveRoleRange"
                EXCLUDE USING gist
                (
                    "SeriesId" WITH =,
                    "RoleKind" WITH =,
                    "RolePosition" WITH =,
                    daterange("EffectiveLocalDate", "EndLocalDate", '[]') WITH &&
                )
                WHERE ("State" IN (0, 1));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "RecurringCommitments" DROP CONSTRAINT IF EXISTS "EX_RecurringCommitments_ActiveRoleRange";""");
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

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringCommitmentRequests_Horizon",
                table: "RecurringCommitmentRequests");
            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringCommitments_Horizon",
                table: "RecurringCommitments");
        }
    }
}
