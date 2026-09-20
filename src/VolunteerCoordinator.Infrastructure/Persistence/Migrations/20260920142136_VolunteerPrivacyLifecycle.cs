using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VolunteerPrivacyLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnonymizedAtUtc",
                table: "Volunteers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusTokenInvalidatedAtUtc",
                table: "ShiftRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_AnonymizedAtUtc",
                table: "Volunteers",
                column: "AnonymizedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Volunteers_AnonymizedAtUtc",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "AnonymizedAtUtc",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "StatusTokenInvalidatedAtUtc",
                table: "ShiftRequests");
        }
    }
}
