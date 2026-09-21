using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Issue22AuditIndexCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");
        }
    }
}
