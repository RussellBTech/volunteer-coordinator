using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Issue22ActiveVolunteerIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Volunteers_NormalizedEmail",
                table: "Volunteers");

            migrationBuilder.DropIndex(
                name: "IX_Volunteers_NormalizedName",
                table: "Volunteers");

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_NormalizedEmail_Active",
                table: "Volunteers",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"AnonymizedAtUtc\" IS NULL");

            migrationBuilder.Sql(
                """CREATE INDEX "IX_Volunteers_NormalizedName" ON "Volunteers" ("NormalizedName" text_pattern_ops) WHERE "AnonymizedAtUtc" IS NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Volunteers_NormalizedEmail_Active",
                table: "Volunteers");

            migrationBuilder.Sql(
                """DROP INDEX "IX_Volunteers_NormalizedName";""");

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_NormalizedEmail",
                table: "Volunteers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_NormalizedName",
                table: "Volunteers",
                column: "NormalizedName");
        }
    }
}
