using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolunteerCoordinator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableAccessAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecoveryTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VolunteerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftSlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryTokens", x => x.Id);
                    table.CheckConstraint("CK_RecoveryTokens_TokenHash", "octet_length(\"TokenHash\") = 32");
                    table.ForeignKey(
                        name: "FK_RecoveryTokens_ShiftSlots_ShiftSlotId",
                        column: x => x.ShiftSlotId,
                        principalTable: "ShiftSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecoveryTokens_Volunteers_VolunteerId",
                        column: x => x.VolunteerId,
                        principalTable: "Volunteers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResendWebhookReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SvixId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderOccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResendWebhookReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VolunteerAccessCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftSlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolunteerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InvalidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IssuedReason = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VolunteerAccessCapabilities", x => x.Id);
                    table.CheckConstraint("CK_VolunteerAccessCapabilities_TokenHash", "octet_length(\"TokenHash\") = 32");
                    table.ForeignKey(
                        name: "FK_VolunteerAccessCapabilities_ShiftSlots_ShiftSlotId",
                        column: x => x.ShiftSlotId,
                        principalTable: "ShiftSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VolunteerAccessCapabilities_Volunteers_VolunteerId",
                        column: x => x.VolunteerId,
                        principalTable: "Volunteers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    TransitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolunteerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RecoveryTokenId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationIntents_RecoveryTokens_RecoveryTokenId",
                        column: x => x.RecoveryTokenId,
                        principalTable: "RecoveryTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NotificationIntents_ShiftSlots_ShiftSlotId",
                        column: x => x.ShiftSlotId,
                        principalTable: "ShiftSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationIntents_Volunteers_VolunteerId",
                        column: x => x.VolunteerId,
                        principalTable: "Volunteers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OutcomeCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveryAttempts_NotificationIntents_Notificati~",
                        column: x => x.NotificationIntentId,
                        principalTable: "NotificationIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_IdempotencyKey",
                table: "NotificationDeliveryAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_NotificationIntentId_Ordinal",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "NotificationIntentId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_EventKey",
                table: "NotificationIntents",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_ProviderMessageId",
                table: "NotificationIntents",
                column: "ProviderMessageId",
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_RecoveryTokenId",
                table: "NotificationIntents",
                column: "RecoveryTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_ShiftSlotId",
                table: "NotificationIntents",
                column: "ShiftSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_State_NextAttemptAtUtc_CreatedAtUtc",
                table: "NotificationIntents",
                columns: new[] { "State", "NextAttemptAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_VolunteerId",
                table: "NotificationIntents",
                column: "VolunteerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryTokens_ShiftSlotId",
                table: "RecoveryTokens",
                column: "ShiftSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryTokens_TokenHash",
                table: "RecoveryTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryTokens_VolunteerId_ShiftSlotId_ExpiresAtUtc",
                table: "RecoveryTokens",
                columns: new[] { "VolunteerId", "ShiftSlotId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResendWebhookReceipts_ProviderMessageId",
                table: "ResendWebhookReceipts",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ResendWebhookReceipts_SvixId",
                table: "ResendWebhookReceipts",
                column: "SvixId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerAccessCapabilities_TokenHash",
                table: "VolunteerAccessCapabilities",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerAccessCapabilities_VolunteerId",
                table: "VolunteerAccessCapabilities",
                column: "VolunteerId");

            migrationBuilder.CreateIndex(
                name: "UX_VolunteerAccessCapabilities_ActiveCommitment",
                table: "VolunteerAccessCapabilities",
                columns: new[] { "ShiftSlotId", "VolunteerId" },
                unique: true,
                filter: "\"InvalidatedAtUtc\" IS NULL");
            migrationBuilder.Sql(
                """
                INSERT INTO "VolunteerAccessCapabilities"
                    ("Id", "ShiftSlotId", "VolunteerId", "TokenHash", "CreatedAtUtc", "InvalidatedAtUtc", "IssuedReason")
                SELECT
                    "Id",
                    "ShiftSlotId",
                    "VolunteerId",
                    "StatusTokenHash",
                    "RequestedAtUtc",
                    CASE
                        WHEN "CapabilityRank" = 1
                             AND "StatusTokenInvalidatedAtUtc" IS NULL
                             AND "VolunteerAnonymizedAtUtc" IS NULL
                             AND "SlotIsActive"
                             AND "ShiftIsActive"
                        THEN NULL
                        ELSE COALESCE("StatusTokenInvalidatedAtUtc", "RequestedAtUtc")
                    END,
                    0
                FROM (
                    SELECT
                        r.*,
                        v."AnonymizedAtUtc" AS "VolunteerAnonymizedAtUtc",
                        ss."IsActive" AS "SlotIsActive",
                        sh."IsActive" AS "ShiftIsActive",
                        ROW_NUMBER() OVER (
                            PARTITION BY r."ShiftSlotId", r."VolunteerId"
                            ORDER BY r."RequestedAtUtc" DESC, r."Id" DESC) AS "CapabilityRank"
                    FROM "ShiftRequests" r
                    INNER JOIN "ShiftSlots" ss ON ss."Id" = r."ShiftSlotId"
                    INNER JOIN "Shifts" sh ON sh."Id" = ss."ShiftId"
                    INNER JOIN "Volunteers" v ON v."Id" = r."VolunteerId"
                    WHERE octet_length(r."StatusTokenHash") = 32
                ) ranked;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "NotificationIntents"
                    ("Id", "EventKey", "TransitionId", "VolunteerId", "ShiftSlotId", "Kind", "State",
                     "CreatedAtUtc", "NextAttemptAtUtc", "CompletedAtUtc", "AttemptCount", "FailureCategory")
                SELECT
                    n."Id",
                    'legacy-attempt:' || n."Id"::text,
                    n."TransitionId",
                    COALESCE(a."VolunteerId", r."VolunteerId"),
                    COALESCE(a."ShiftSlotId", r."ShiftSlotId"),
                    n."Kind",
                    7,
                    n."CreatedAtUtc",
                    n."CreatedAtUtc",
                    COALESCE(n."CompletedAtUtc", n."CreatedAtUtc"),
                    1,
                    'MigratedUnavailable'
                FROM "NotificationAttempts" n
                LEFT JOIN "Assignments" a ON a."Id" = n."TransitionId"
                LEFT JOIN "ShiftRequests" r ON r."Id" = n."TransitionId"
                WHERE COALESCE(a."VolunteerId", r."VolunteerId") IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "ResendWebhookReceipts");

            migrationBuilder.DropTable(
                name: "VolunteerAccessCapabilities");

            migrationBuilder.DropTable(
                name: "NotificationIntents");

            migrationBuilder.DropTable(
                name: "RecoveryTokens");
        }
    }
}
