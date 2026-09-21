using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrectionGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationIntents_EventKey",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "Destination",
                table: "NotificationAttempts");

            migrationBuilder.AddColumn<Guid>(
                name: "ClaimOwnerToken",
                table: "NotificationIntents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastProviderEventAtUtc",
                table: "NotificationIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "NotificationIntents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_EventKey",
                table: "NotificationIntents",
                column: "EventKey",
                unique: true,
                filter: "\"State\" IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException(
                "CorrectionGate irreversibly removes legacy notification destinations.");
    }
}
