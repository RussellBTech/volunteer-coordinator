using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAccessCutover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftRequests_StatusTokenHash",
                table: "ShiftRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftRequests_StatusTokenHash",
                table: "ShiftRequests");

            migrationBuilder.DropColumn(
                name: "StatusTokenExpiresAtUtc",
                table: "ShiftRequests");

            migrationBuilder.DropColumn(
                name: "StatusTokenHash",
                table: "ShiftRequests");

            migrationBuilder.DropColumn(
                name: "StatusTokenInvalidatedAtUtc",
                table: "ShiftRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusTokenExpiresAtUtc",
                table: "ShiftRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<byte[]>(
                name: "StatusTokenHash",
                table: "ShiftRequests",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusTokenInvalidatedAtUtc",
                table: "ShiftRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftRequests_StatusTokenHash",
                table: "ShiftRequests",
                column: "StatusTokenHash",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftRequests_StatusTokenHash",
                table: "ShiftRequests",
                sql: "octet_length(\"StatusTokenHash\") = 32");
        }
    }
}
