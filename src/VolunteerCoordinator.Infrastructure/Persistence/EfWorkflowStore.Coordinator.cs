using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Auditing;

namespace VolunteerCoordinator.Infrastructure.Persistence;

public sealed partial class EfWorkflowStore
{
    private const string CoordinatorWorkCte = """
        WITH work AS (
            SELECT
                request."Id" AS "StableId",
                'pending-request' AS "Category",
                4 AS "CategoryRank",
                CASE WHEN shift."StartsAtUtc" <= @urgent_until THEN 0
                     WHEN shift."StartsAtUtc" <= @soon_until THEN 1 ELSE 2 END AS "SeverityRank",
                shift."StartsAtUtc" AS "DueAtUtc",
                shift."StartsAtUtc" AS "StartsAtUtc",
                shift."EndsAtUtc" AS "EndsAtUtc",
                shift."Id" AS "ShiftId",
                slot."Id" AS "SlotId",
                volunteer."Id" AS "VolunteerId",
                NULL::uuid AS "SeriesId",
                NULL::uuid AS "CommitmentId",
                shift."Title" AS "Title",
                CASE WHEN slot."Kind" = 0 THEN 'Primary' ELSE 'Backup ' || slot."Position"::text END AS "Context",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "PersonName",
                'Request to review' AS "State",
                'request' AS "RouteKind",
                request."Id" AS "RouteId"
            FROM "ShiftRequests" request
            JOIN "ShiftSlots" slot ON slot."Id" = request."ShiftSlotId"
            JOIN "Shifts" shift ON shift."Id" = slot."ShiftId"
            JOIN "Volunteers" volunteer ON volunteer."Id" = request."VolunteerId"
            WHERE request."Status" = 0
              AND slot."IsActive"
              AND shift."IsActive"
              AND shift."EndsAtUtc" > @now

            UNION ALL

            SELECT
                slot."Id" AS "StableId",
                'uncovered' AS "Category",
                1 AS "CategoryRank",
                CASE WHEN shift."StartsAtUtc" <= @urgent_until THEN 0
                     WHEN shift."StartsAtUtc" <= @soon_until THEN 1 ELSE 2 END AS "SeverityRank",
                shift."StartsAtUtc" AS "DueAtUtc",
                shift."StartsAtUtc" AS "StartsAtUtc",
                shift."EndsAtUtc" AS "EndsAtUtc",
                shift."Id" AS "ShiftId",
                slot."Id" AS "SlotId",
                NULL::uuid AS "VolunteerId",
                NULL::uuid AS "SeriesId",
                NULL::uuid AS "CommitmentId",
                shift."Title" AS "Title",
                CASE WHEN slot."Kind" = 0 THEN 'Primary' ELSE 'Backup ' || slot."Position"::text END AS "Context",
                NULL::text AS "PersonName",
                'Open commitment' AS "State",
                'coverage' AS "RouteKind",
                slot."Id" AS "RouteId"
            FROM "ShiftSlots" slot
            JOIN "Shifts" shift ON shift."Id" = slot."ShiftId"
            WHERE slot."IsActive"
              AND shift."IsActive"
              AND shift."PublishedAtUtc" IS NOT NULL
              AND shift."EndsAtUtc" > @now
              AND NOT EXISTS (
                  SELECT 1
                  FROM "Assignments" assignment
                  WHERE assignment."ShiftSlotId" = slot."Id"
                    AND assignment."Status" IN (0, 1))

            UNION ALL

            SELECT
                assignment."Id" AS "StableId",
                'unconfirmed' AS "Category",
                3 AS "CategoryRank",
                CASE WHEN shift."StartsAtUtc" <= @urgent_until THEN 0
                     WHEN shift."StartsAtUtc" <= @soon_until THEN 1 ELSE 2 END AS "SeverityRank",
                shift."StartsAtUtc" AS "DueAtUtc",
                shift."StartsAtUtc" AS "StartsAtUtc",
                shift."EndsAtUtc" AS "EndsAtUtc",
                shift."Id" AS "ShiftId",
                slot."Id" AS "SlotId",
                volunteer."Id" AS "VolunteerId",
                NULL::uuid AS "SeriesId",
                NULL::uuid AS "CommitmentId",
                shift."Title" AS "Title",
                CASE WHEN slot."Kind" = 0 THEN 'Primary' ELSE 'Backup ' || slot."Position"::text END AS "Context",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "PersonName",
                'Waiting for confirmation' AS "State",
                'coverage-unconfirmed' AS "RouteKind",
                slot."Id" AS "RouteId"
            FROM "Assignments" assignment
            JOIN "ShiftSlots" slot ON slot."Id" = assignment."ShiftSlotId"
            JOIN "Shifts" shift ON shift."Id" = assignment."ShiftId"
            JOIN "Volunteers" volunteer ON volunteer."Id" = assignment."VolunteerId"
            WHERE assignment."Status" = 0
              AND slot."IsActive"
              AND shift."IsActive"
              AND shift."PublishedAtUtc" IS NOT NULL
              AND shift."EndsAtUtc" > @now

            UNION ALL

            SELECT message_work.*
            FROM (
                SELECT DISTINCT ON (source."TransitionId", source."Kind")
                    source."TransitionId" AS "StableId",
                    'message' AS "Category",
                    2 AS "CategoryRank",
                    CASE WHEN shift."StartsAtUtc" <= @urgent_until THEN 0
                         WHEN shift."StartsAtUtc" <= @soon_until THEN 1 ELSE 2 END AS "SeverityRank",
                    shift."StartsAtUtc" AS "DueAtUtc",
                    shift."StartsAtUtc" AS "StartsAtUtc",
                    shift."EndsAtUtc" AS "EndsAtUtc",
                    shift."Id" AS "ShiftId",
                    slot."Id" AS "SlotId",
                    volunteer."Id" AS "VolunteerId",
                    NULL::uuid AS "SeriesId",
                    NULL::uuid AS "CommitmentId",
                    shift."Title" AS "Title",
                    CASE WHEN slot."Kind" = 0 THEN 'Primary' ELSE 'Backup ' || slot."Position"::text END AS "Context",
                    CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "PersonName",
                    'Message needs follow-up' AS "State",
                    'messages' AS "RouteKind",
                    shift."Id" AS "RouteId"
                FROM (
                    SELECT
                        intent."TransitionId" AS "TransitionId",
                        intent."Kind" AS "Kind",
                        intent."Id" AS "SourceId",
                        intent."ShiftSlotId" AS "ShiftSlotId",
                        intent."VolunteerId" AS "VolunteerId",
                        intent."CreatedAtUtc" AS "CreatedAtUtc"
                    FROM "NotificationIntents" intent
                    WHERE intent."State" IN (0, 1, 2, 5, 6, 7)
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "NotificationAttempts" existing_attempt
                          WHERE existing_attempt."TransitionId" = intent."TransitionId"
                            AND existing_attempt."State" = 2)

                    UNION ALL

                    SELECT
                        attempt."TransitionId" AS "TransitionId",
                        attempt."Kind" AS "Kind",
                        attempt."Id" AS "SourceId",
                        assignment."ShiftSlotId" AS "ShiftSlotId",
                        assignment."VolunteerId" AS "VolunteerId",
                        attempt."CreatedAtUtc" AS "CreatedAtUtc"
                    FROM "NotificationAttempts" attempt
                    JOIN "Assignments" assignment ON assignment."Id" = attempt."TransitionId"
                    WHERE attempt."State" = 2

                    UNION ALL

                    SELECT
                        attempt."TransitionId" AS "TransitionId",
                        attempt."Kind" AS "Kind",
                        attempt."Id" AS "SourceId",
                        request."ShiftSlotId" AS "ShiftSlotId",
                        request."VolunteerId" AS "VolunteerId",
                        attempt."CreatedAtUtc" AS "CreatedAtUtc"
                    FROM "NotificationAttempts" attempt
                    JOIN "ShiftRequests" request ON request."Id" = attempt."TransitionId"
                    WHERE attempt."State" = 2
                ) source
                JOIN "ShiftSlots" slot ON slot."Id" = source."ShiftSlotId"
                JOIN "Shifts" shift ON shift."Id" = slot."ShiftId"
                JOIN "Volunteers" volunteer ON volunteer."Id" = source."VolunteerId"
                WHERE slot."IsActive"
                  AND shift."IsActive"
                  AND shift."EndsAtUtc" > @now
                ORDER BY source."TransitionId", source."Kind", source."CreatedAtUtc" DESC, source."SourceId" DESC
            ) message_work

            UNION ALL

            SELECT
                commitment."Id" AS "StableId",
                'withdrawal' AS "Category",
                5 AS "CategoryRank",
                2 AS "SeverityRank",
                COALESCE(occurrence_shift."StartsAtUtc", @soon_until) AS "DueAtUtc",
                occurrence_shift."StartsAtUtc" AS "StartsAtUtc",
                occurrence_shift."EndsAtUtc" AS "EndsAtUtc",
                occurrence_shift."Id" AS "ShiftId",
                occurrence_slot."Id" AS "SlotId",
                volunteer."Id" AS "VolunteerId",
                commitment."SeriesId" AS "SeriesId",
                commitment."Id" AS "CommitmentId",
                revision."Title" AS "Title",
                'Future withdrawal needs coverage review' AS "Context",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "PersonName",
                'Recurring withdrawal risk' AS "State",
                'handoff' AS "RouteKind",
                commitment."Id" AS "RouteId"
            FROM "RecurringCommitments" commitment
            JOIN "RecurringShiftSeriesRevisions" revision ON revision."Id" = commitment."RevisionId"
            JOIN "Volunteers" volunteer ON volunteer."Id" = commitment."VolunteerId"
            JOIN LATERAL (
                SELECT shift."Id", shift."StartsAtUtc", shift."EndsAtUtc"
                FROM "RecurringShiftOccurrences" occurrence
                JOIN "Shifts" shift ON shift."Id" = occurrence."ShiftId"
                WHERE occurrence."SeriesId" = commitment."SeriesId"
                  AND occurrence."LocalDate" >= COALESCE(commitment."WithdrawalEffectiveLocalDate", commitment."EffectiveLocalDate")
                  AND occurrence."LocalDate" <= commitment."EndLocalDate"
                  AND shift."StartsAtUtc" > @now
                  AND shift."IsActive"
                ORDER BY occurrence."LocalDate", occurrence."Id"
                LIMIT 1
            ) occurrence_shift ON TRUE
            JOIN "ShiftSlots" occurrence_slot
              ON occurrence_slot."ShiftId" = occurrence_shift."Id"
             AND occurrence_slot."Kind" = commitment."RoleKind"
             AND occurrence_slot."Position" = commitment."RolePosition"
             AND occurrence_slot."IsActive"
            WHERE commitment."State" IN (0, 1)
              AND commitment."WithdrawalEffectiveLocalDate" IS NOT NULL
              AND occurrence_shift."Id" IS NOT NULL

            UNION ALL

            SELECT
                request."Id" AS "StableId",
                'pending-request' AS "Category",
                4 AS "CategoryRank",
                CASE WHEN COALESCE(request_shift."StartsAtUtc", @soon_until) <= @urgent_until THEN 0
                     WHEN COALESCE(request_shift."StartsAtUtc", @soon_until) <= @soon_until THEN 1 ELSE 2 END AS "SeverityRank",
                COALESCE(request_shift."StartsAtUtc", @soon_until) AS "DueAtUtc",
                request_shift."StartsAtUtc" AS "StartsAtUtc",
                request_shift."EndsAtUtc" AS "EndsAtUtc",
                request_shift."Id" AS "ShiftId",
                request_slot."Id" AS "SlotId",
                volunteer."Id" AS "VolunteerId",
                request."SeriesId" AS "SeriesId",
                NULL::uuid AS "CommitmentId",
                revision."Title" AS "Title",
                'Recurring request to review' AS "Context",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "PersonName",
                'Request to review' AS "State",
                'request' AS "RouteKind",
                request."Id" AS "RouteId"
            FROM "RecurringCommitmentRequests" request
            JOIN "RecurringShiftSeriesRevisions" revision ON revision."Id" = request."RevisionId"
            JOIN "Volunteers" volunteer ON volunteer."Id" = request."VolunteerId"
            LEFT JOIN LATERAL (
                SELECT shift."Id", shift."StartsAtUtc", shift."EndsAtUtc"
                FROM "RecurringShiftOccurrences" occurrence
                JOIN "Shifts" shift ON shift."Id" = occurrence."ShiftId"
                WHERE occurrence."SeriesId" = request."SeriesId"
                  AND occurrence."LocalDate" BETWEEN request."EffectiveLocalDate" AND request."EndLocalDate"
                  AND shift."EndsAtUtc" > @now
                  AND shift."IsActive"
                ORDER BY occurrence."LocalDate", occurrence."Id"
                LIMIT 1
            ) request_shift ON TRUE
            LEFT JOIN "ShiftSlots" request_slot ON request_slot."ShiftId" = request_shift."Id"
                AND request_slot."Kind" = request."RoleKind"
                AND request_slot."Position" = request."RolePosition"
            WHERE request."Status" = 0
              AND request."EndLocalDate" >= (@now AT TIME ZONE @group_zone)::date

            UNION ALL

            SELECT
                occurrence."Id" AS "StableId",
                'recurrence-review' AS "Category",
                6 AS "CategoryRank",
                2 AS "SeverityRank",
                @soon_until AS "DueAtUtc",
                NULL::timestamptz AS "StartsAtUtc",
                @soon_until AS "EndsAtUtc",
                NULL::uuid AS "ShiftId",
                NULL::uuid AS "SlotId",
                NULL::uuid AS "VolunteerId",
                occurrence."SeriesId" AS "SeriesId",
                NULL::uuid AS "CommitmentId",
                revision."Title" AS "Title",
                'A local schedule gap needs a decision' AS "Context",
                NULL::text AS "PersonName",
                'Recurring schedule review' AS "State",
                'recurring' AS "RouteKind",
                occurrence."Id" AS "RouteId"
            FROM "RecurringShiftOccurrences" occurrence
            JOIN "RecurringShiftSeries" series ON series."Id" = occurrence."SeriesId"
            JOIN "RecurringShiftSeriesRevisions" revision ON revision."Id" = occurrence."RevisionId"
            WHERE series."IsActive"
              AND occurrence."Status" = 1

            UNION ALL

            SELECT
                series."Id" AS "StableId",
                'zone-review' AS "Category",
                7 AS "CategoryRank",
                2 AS "SeverityRank",
                @soon_until AS "DueAtUtc",
                NULL::timestamptz AS "StartsAtUtc",
                @soon_until AS "EndsAtUtc",
                NULL::uuid AS "ShiftId",
                NULL::uuid AS "SlotId",
                NULL::uuid AS "VolunteerId",
                series."Id" AS "SeriesId",
                NULL::uuid AS "CommitmentId",
                revision."Title" AS "Title",
                'The recurring schedule uses a different time zone' AS "Context",
                NULL::text AS "PersonName",
                'Time-zone review' AS "State",
                'recurring' AS "RouteKind",
                series."Id" AS "RouteId"
            FROM "RecurringShiftSeries" series
            JOIN "RecurringShiftSeriesRevisions" revision
              ON revision."SeriesId" = series."Id"
             AND revision."RevisionNumber" = series."CurrentRevisionNumber"
            WHERE series."IsActive"
              AND revision."TimeZoneId" <> @group_zone

            UNION ALL

            SELECT
                commitment."Id" AS "StableId",
                'handoff' AS "Category",
                8 AS "CategoryRank",
                2 AS "SeverityRank",
                COALESCE(handoff_shift."StartsAtUtc", @soon_until) AS "DueAtUtc",
                handoff_shift."StartsAtUtc" AS "StartsAtUtc",
                handoff_shift."EndsAtUtc" AS "EndsAtUtc",
                handoff_shift."Id" AS "ShiftId",
                NULL::uuid AS "SlotId",
                volunteer."Id" AS "VolunteerId",
                commitment."SeriesId" AS "SeriesId",
                commitment."Id" AS "CommitmentId",
                current_revision."Title" AS "Title",
                'A recurring commitment needs explicit schedule handoff' AS "Context",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "PersonName",
                'Recurring handoff' AS "State",
                'handoff' AS "RouteKind",
                commitment."Id" AS "RouteId"
            FROM "RecurringCommitments" commitment
            JOIN "RecurringShiftSeries" series ON series."Id" = commitment."SeriesId"
            JOIN "RecurringShiftSeriesRevisions" old_revision ON old_revision."Id" = commitment."RevisionId"
            JOIN "RecurringShiftSeriesRevisions" current_revision
              ON current_revision."SeriesId" = series."Id"
             AND current_revision."RevisionNumber" = series."CurrentRevisionNumber"
            JOIN "Volunteers" volunteer ON volunteer."Id" = commitment."VolunteerId"
            JOIN LATERAL (
                SELECT shift."Id", shift."StartsAtUtc", shift."EndsAtUtc"
                FROM "RecurringShiftOccurrences" occurrence
                JOIN "Shifts" shift ON shift."Id" = occurrence."ShiftId"
                WHERE occurrence."SeriesId" = commitment."SeriesId"
                  AND occurrence."LocalDate" >= commitment."EffectiveLocalDate"
                  AND occurrence."LocalDate" <= commitment."EndLocalDate"
                  AND shift."StartsAtUtc" > @now
                  AND shift."IsActive"
                ORDER BY occurrence."LocalDate", occurrence."Id"
                LIMIT 1
            ) handoff_shift ON TRUE
            WHERE series."IsActive"
              AND commitment."State" IN (0, 1)
              AND old_revision."RevisionNumber" <> series."CurrentRevisionNumber"
        )
        """;

    private const string AuditHistoryCte = """
        WITH history AS (
            SELECT
                audit."Id" AS "Id",
                audit."OccurredAtUtc" AS "OccurredAtUtc",
                audit."Actor" AS "Actor",
                audit."Action" AS "Action",
                CASE
                    WHEN audit."Action" IN ('AssignmentConfirm', 'AssignmentConfirmed', 'AssignmentDecline', 'AssignmentDeclined', 'AssignmentCancel', 'AssignmentCancelled') THEN 'Volunteer actions'
                    WHEN audit."Action" LIKE 'GroupTimeZone%' OR audit."Action" LIKE 'Shift%' THEN 'Schedule'
                    WHEN audit."Action" LIKE 'Request%' THEN 'Requests'
                    WHEN audit."Action" LIKE 'Assignment%' THEN 'Assignments'
                    WHEN audit."Action" LIKE 'VolunteerAccess%' OR audit."Action" = 'CoordinatorAccessVerified' THEN 'Access'
                    WHEN audit."Action" LIKE 'VolunteerAnonymized%' THEN 'Privacy'
                    WHEN audit."Action" LIKE 'Notification%' OR audit."Action" LIKE '%Message%' THEN 'Messages'
                    WHEN audit."Action" LIKE 'RecurringCommitment%' THEN 'Recurring commitments'
                    WHEN audit."Action" LIKE 'RecurringSeries%' OR audit."Action" LIKE 'RecurringOccurrence%' OR audit."Action" LIKE 'RecurringProtected%' THEN 'Recurring schedules'
                    ELSE 'Other'
                END AS "Category",
                audit."ShiftId" AS "ShiftId",
                audit."VolunteerId" AS "VolunteerId",
                shift."Title" AS "ShiftTitle",
                shift."StartsAtUtc" AS "ShiftStartsAtUtc",
                volunteer."Name" AS "VolunteerName",
                volunteer."AnonymizedAtUtc" IS NOT NULL AS "VolunteerIsAnonymized"
            FROM "AuditEntries" audit
            LEFT JOIN "Shifts" shift ON shift."Id" = audit."ShiftId"
            LEFT JOIN "Volunteers" volunteer ON volunteer."Id" = audit."VolunteerId"
            WHERE (@from_utc IS NULL OR audit."OccurredAtUtc" >= @from_utc)
              AND (@through_utc IS NULL OR audit."OccurredAtUtc" < @through_utc)
              AND (@actor = '' OR UPPER(audit."Actor") = @actor)
              AND (@shift_id IS NULL OR audit."ShiftId" = @shift_id)
              AND (@volunteer_id IS NULL OR audit."VolunteerId" = @volunteer_id)
        )
        """;

    public async Task<IReadOnlyList<CoordinatorCoverageQueryRow>> GetCoordinatorCoveragePageAsync(
        DateTimeOffset nowUtc,
        string? attention,
        int limit,
        CancellationToken cancellationToken)
    {
        var attentionPredicate = attention switch
        {
            "uncovered" => """AND assignment."Id" IS NULL""",
            "unconfirmed" => """AND assignment."Status" = 0""",
            _ => string.Empty
        };
        var sql = $"""
            SELECT
                slot."Id" AS "SlotId",
                shift."Id" AS "ShiftId",
                assignment."Id" AS "AssignmentId",
                assignment."VolunteerId" AS "VolunteerId",
                shift."Title" AS "ShiftTitle",
                shift."StartsAtUtc" AS "StartsAtUtc",
                shift."EndsAtUtc" AS "EndsAtUtc",
                shift."Location" AS "Location",
                shift."VolunteerInstructions" AS "VolunteerInstructions",
                slot."Kind" AS "SlotKind",
                slot."Position" AS "SlotPosition",
                shift."SignupPolicy" AS "SignupPolicy",
                CASE assignment."Status"
                    WHEN 0 THEN 'Unconfirmed'
                    WHEN 1 THEN 'Confirmed'
                    ELSE 'Uncovered'
                END AS "State",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "VolunteerName",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Email" ELSE NULL END AS "VolunteerEmail",
                CASE
                    WHEN assignment."Id" IS NULL THEN NULL
                    WHEN latest_access."State" IN (5, 6, 7) THEN 'Message not sent'
                    WHEN latest_access."State" IN (0, 1, 2) THEN 'Delivery pending'
                    WHEN EXISTS (
                        SELECT 1
                        FROM "VolunteerAccessCapabilities" capability
                        WHERE capability."VolunteerId" = assignment."VolunteerId"
                          AND capability."ShiftSlotId" = slot."Id"
                          AND capability."InvalidatedAtUtc" IS NULL)
                         AND @now_utc <= shift."EndsAtUtc" + INTERVAL '7 days' THEN 'Active'
                    WHEN @now_utc > shift."EndsAtUtc" + INTERVAL '7 days' THEN 'Expired'
                    ELSE 'Revoked'
                END AS "AccessState",
                latest_access."CreatedAtUtc" AS "AccessLastMessageAtUtc"
            FROM "ShiftSlots" slot
            JOIN "Shifts" shift ON shift."Id" = slot."ShiftId"
            LEFT JOIN "Assignments" assignment
              ON assignment."ShiftSlotId" = slot."Id"
             AND assignment."Status" IN (0, 1)
            LEFT JOIN "Volunteers" volunteer ON volunteer."Id" = assignment."VolunteerId"
            LEFT JOIN LATERAL (
                SELECT intent."State", intent."CreatedAtUtc"
                FROM "NotificationIntents" intent
                WHERE intent."VolunteerId" = assignment."VolunteerId"
                  AND intent."ShiftSlotId" = slot."Id"
                  AND (
                      intent."Kind" = 'RequestReceipt'
                      OR intent."Kind" ILIKE '%Access%'
                      OR intent."Kind" ILIKE '%Recovery%'
                      OR intent."Kind" ILIKE '%Reissue%')
                ORDER BY intent."CreatedAtUtc" DESC, intent."Id" DESC
                LIMIT 1
            ) latest_access ON TRUE
            WHERE slot."IsActive"
              AND shift."IsActive"
              AND shift."PublishedAtUtc" IS NOT NULL
              AND shift."EndsAtUtc" > @now_utc
              {attentionPredicate}
            ORDER BY shift."StartsAtUtc",
                     CASE
                         WHEN assignment."Id" IS NULL THEN 0
                         WHEN assignment."Status" = 0 THEN 1
                         ELSE 2
                     END,
                     slot."Kind",
                     slot."Position",
                     slot."Id"
            LIMIT @page_limit
            """;
        var parameters = new[]
        {
            new NpgsqlParameter("now_utc", NpgsqlDbType.TimestampTz) { Value = nowUtc },
            new NpgsqlParameter("page_limit", NpgsqlDbType.Integer) { Value = Math.Clamp(limit, 1, 50) }
        };
        return await _dbContext.CoordinatorCoverageQueryRows
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<CoordinatorCoverageQueryRow?> GetCoordinatorCoverageSlotAsync(
        DateTimeOffset nowUtc,
        Guid slotId,
        CancellationToken cancellationToken)
    {
        var rows = await GetCoordinatorCoveragePageBySlotAsync(nowUtc, slotId, cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<IReadOnlyList<CoordinatorCoverageQueryRow>> GetCoordinatorCoveragePageBySlotAsync(
        DateTimeOffset nowUtc,
        Guid slotId,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT
                slot."Id" AS "SlotId",
                shift."Id" AS "ShiftId",
                assignment."Id" AS "AssignmentId",
                assignment."VolunteerId" AS "VolunteerId",
                shift."Title" AS "ShiftTitle",
                shift."StartsAtUtc" AS "StartsAtUtc",
                shift."EndsAtUtc" AS "EndsAtUtc",
                shift."Location" AS "Location",
                shift."VolunteerInstructions" AS "VolunteerInstructions",
                slot."Kind" AS "SlotKind",
                slot."Position" AS "SlotPosition",
                shift."SignupPolicy" AS "SignupPolicy",
                CASE assignment."Status"
                    WHEN 0 THEN 'Unconfirmed'
                    WHEN 1 THEN 'Confirmed'
                    ELSE 'Uncovered'
                END AS "State",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "VolunteerName",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Email" ELSE NULL END AS "VolunteerEmail",
                CASE
                    WHEN assignment."Id" IS NULL THEN NULL
                    WHEN latest_access."State" IN (5, 6, 7) THEN 'Message not sent'
                    WHEN latest_access."State" IN (0, 1, 2) THEN 'Delivery pending'
                    WHEN EXISTS (
                        SELECT 1
                        FROM "VolunteerAccessCapabilities" capability
                        WHERE capability."VolunteerId" = assignment."VolunteerId"
                          AND capability."ShiftSlotId" = slot."Id"
                          AND capability."InvalidatedAtUtc" IS NULL)
                         AND @now_utc <= shift."EndsAtUtc" + INTERVAL '7 days' THEN 'Active'
                    WHEN @now_utc > shift."EndsAtUtc" + INTERVAL '7 days' THEN 'Expired'
                    ELSE 'Revoked'
                END AS "AccessState",
                latest_access."CreatedAtUtc" AS "AccessLastMessageAtUtc"
            FROM "ShiftSlots" slot
            JOIN "Shifts" shift ON shift."Id" = slot."ShiftId"
            LEFT JOIN "Assignments" assignment
              ON assignment."ShiftSlotId" = slot."Id"
             AND assignment."Status" IN (0, 1)
            LEFT JOIN "Volunteers" volunteer ON volunteer."Id" = assignment."VolunteerId"
            LEFT JOIN LATERAL (
                SELECT intent."State", intent."CreatedAtUtc"
                FROM "NotificationIntents" intent
                WHERE intent."VolunteerId" = assignment."VolunteerId"
                  AND intent."ShiftSlotId" = slot."Id"
                  AND (
                      intent."Kind" = 'RequestReceipt'
                      OR intent."Kind" ILIKE '%Access%'
                      OR intent."Kind" ILIKE '%Recovery%'
                      OR intent."Kind" ILIKE '%Reissue%')
                ORDER BY intent."CreatedAtUtc" DESC, intent."Id" DESC
                LIMIT 1
            ) latest_access ON TRUE
            WHERE slot."Id" = @slot_id
            """;
        var parameters = new[]
        {
            new NpgsqlParameter("now_utc", NpgsqlDbType.TimestampTz) { Value = nowUtc },
            new NpgsqlParameter("slot_id", NpgsqlDbType.Uuid) { Value = slotId }
        };
        return await _dbContext.CoordinatorCoverageQueryRows
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CoordinatorRequestQueryRow>> GetCoordinatorRequestPageAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT
                request."Id" AS "RequestId",
                volunteer."Id" AS "VolunteerId",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "VolunteerName",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Email" ELSE '' END AS "VolunteerEmail",
                shift."Id" AS "ShiftId",
                slot."Id" AS "SlotId",
                shift."Title" AS "ShiftTitle",
                shift."StartsAtUtc" AS "StartsAtUtc",
                shift."EndsAtUtc" AS "EndsAtUtc",
                shift."Location" AS "Location",
                shift."VolunteerInstructions" AS "VolunteerInstructions",
                slot."Kind" AS "SlotKind",
                slot."Position" AS "SlotPosition",
                shift."SignupPolicy" AS "SignupPolicy",
                request."Status" AS "RequestStatus",
                request."RequestedAtUtc" AS "RequestedAtUtc",
                CASE
                    WHEN request."Status" <> 0 OR NOT slot."IsActive" OR NOT shift."IsActive" OR shift."EndsAtUtc" <= @now_utc THEN FALSE
                    WHEN assignment."Id" IS NULL THEN TRUE
                    ELSE FALSE
                END AS "CanApprove",
                CASE
                    WHEN NOT slot."IsActive" OR NOT shift."IsActive" THEN 'Inactive'
                    WHEN shift."EndsAtUtc" <= @now_utc THEN 'Ended'
                    WHEN assignment."Status" = 0 THEN 'Unconfirmed'
                    WHEN assignment."Status" = 1 THEN 'Confirmed'
                    ELSE 'Available'
                END AS "SlotState"
            FROM "ShiftRequests" request
            JOIN "ShiftSlots" slot ON slot."Id" = request."ShiftSlotId"
            JOIN "Shifts" shift ON shift."Id" = slot."ShiftId"
            JOIN "Volunteers" volunteer ON volunteer."Id" = request."VolunteerId"
            LEFT JOIN "Assignments" assignment
              ON assignment."ShiftSlotId" = slot."Id"
             AND assignment."Status" IN (0, 1)
            ORDER BY request."RequestedAtUtc" DESC, request."Id"
            LIMIT @page_limit
            """;
        var parameters = new[]
        {
            new NpgsqlParameter("now_utc", NpgsqlDbType.TimestampTz) { Value = nowUtc },
            new NpgsqlParameter("page_limit", NpgsqlDbType.Integer) { Value = Math.Clamp(limit, 1, 50) }
        };
        return await _dbContext.CoordinatorRequestQueryRows
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CoordinatorNotificationIntentQueryRow>> GetCoordinatorNotificationIntentPageAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT
                intent."Id" AS "Id",
                intent."VolunteerId" AS "VolunteerId",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "VolunteerName",
                intent."ShiftSlotId" AS "ShiftSlotId",
                shift."Id" AS "ShiftId",
                shift."Title" AS "ShiftTitle",
                shift."StartsAtUtc" AS "StartsAtUtc",
                shift."EndsAtUtc" AS "EndsAtUtc",
                shift."Location" AS "Location",
                shift."VolunteerInstructions" AS "VolunteerInstructions",
                COALESCE(slot."Kind", 0) AS "SlotKind",
                COALESCE(slot."Position", 1) AS "SlotPosition",
                COALESCE(shift."SignupPolicy", 0) AS "SignupPolicy",
                settings."TimeZoneId" AS "GroupTimeZoneId",
                intent."State" AS "State",
                intent."Kind" AS "Kind",
                intent."CreatedAtUtc" AS "CreatedAtUtc",
                intent."NextAttemptAtUtc" AS "NextAttemptAtUtc",
                intent."AttemptCount" AS "AttemptCount",
                intent."FailureCategory" AS "FailureCategory",
                CASE
                    WHEN intent."Kind" = 'RequestReceipt'
                      OR intent."Kind" ILIKE '%Access%'
                      OR intent."Kind" ILIKE '%Recovery%'
                      OR intent."Kind" ILIKE '%Reissue%'
                    THEN active_assignment."Id"
                    ELSE NULL
                END AS "AccessAssignmentId"
            FROM "NotificationIntents" intent
            JOIN "Volunteers" volunteer ON volunteer."Id" = intent."VolunteerId"
            CROSS JOIN "GroupSettings" settings
            LEFT JOIN "ShiftSlots" slot ON slot."Id" = intent."ShiftSlotId"
            LEFT JOIN "Shifts" shift ON shift."Id" = slot."ShiftId"
            LEFT JOIN "Assignments" active_assignment
              ON active_assignment."ShiftSlotId" = intent."ShiftSlotId"
             AND active_assignment."VolunteerId" = intent."VolunteerId"
             AND active_assignment."Status" IN (0, 1)
            ORDER BY intent."CreatedAtUtc" DESC, intent."Id" DESC
            LIMIT @page_limit
            """;
        var parameters = new[]
        {
            new NpgsqlParameter("page_limit", NpgsqlDbType.Integer) { Value = Math.Clamp(limit, 1, 50) }
        };
        return await _dbContext.CoordinatorNotificationIntentQueryRows
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CoordinatorRecurringRequestQueryRow>> GetCoordinatorRecurringRequestPageAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT
                request_row."Id" AS "RequestId",
                request_row."VolunteerId" AS "VolunteerId",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Name" ELSE 'Removed volunteer' END AS "VolunteerName",
                CASE WHEN volunteer."AnonymizedAtUtc" IS NULL THEN volunteer."Email" ELSE '' END AS "VolunteerEmail",
                revision."Title" AS "Title",
                request_row."RoleKind" AS "RoleKind",
                request_row."RolePosition" AS "RolePosition",
                request_row."SourcePolicy" AS "SourcePolicy",
                request_row."Status" AS "Status",
                request_row."EffectiveLocalDate" AS "EffectiveLocalDate",
                request_row."EndLocalDate" AS "EndLocalDate",
                COALESCE(candidates."IncludedCount", 0) AS "IncludedCount",
                COALESCE(candidates."TotalCount", 0) AS "TotalCount",
                request_row."RecurringCommitmentId" AS "RecurringCommitmentId"
            FROM "RecurringCommitmentRequests" request_row
            JOIN "RecurringShiftSeriesRevisions" revision ON revision."Id" = request_row."RevisionId"
            JOIN "Volunteers" volunteer ON volunteer."Id" = request_row."VolunteerId"
            LEFT JOIN LATERAL (
                SELECT
                    COUNT(*)::integer AS "TotalCount",
                    COUNT(*) FILTER (
                        WHERE occurrence."Status" = 0
                          AND NOT occurrence."IsException"
                          AND shift."IsActive"
                          AND shift."PublishedAtUtc" IS NOT NULL
                          AND shift."StartsAtUtc" > @now_utc
                          AND slot."Id" IS NOT NULL
                          AND NOT EXISTS (
                              SELECT 1
                              FROM "Assignments" assignment
                              WHERE assignment."ShiftSlotId" = slot."Id"
                                AND assignment."Status" IN (0, 1)))::integer AS "IncludedCount"
                FROM "RecurringShiftOccurrences" occurrence
                LEFT JOIN "Shifts" shift ON shift."Id" = occurrence."ShiftId"
                LEFT JOIN "ShiftSlots" slot
                  ON slot."ShiftId" = shift."Id"
                 AND slot."Kind" = request_row."RoleKind"
                 AND slot."Position" = request_row."RolePosition"
                 AND slot."IsActive"
                WHERE occurrence."SeriesId" = request_row."SeriesId"
                  AND occurrence."LocalDate" BETWEEN request_row."EffectiveLocalDate" AND request_row."EndLocalDate"
            ) candidates ON TRUE
            ORDER BY request_row."RequestedAtUtc" DESC, request_row."Id"
            LIMIT @page_limit
            """;
        var parameters = new[]
        {
            new NpgsqlParameter("now_utc", NpgsqlDbType.TimestampTz) { Value = nowUtc },
            new NpgsqlParameter("page_limit", NpgsqlDbType.Integer) { Value = Math.Clamp(limit, 1, 50) }
        };
        return await _dbContext.CoordinatorRecurringRequestQueryRows
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<CoordinatorWorkSummaryDto> GetCoordinatorWorkSummaryAsync(
        DateTimeOffset nowUtc,
        CoordinatorAttentionOptions options,
        CancellationToken cancellationToken)
    {
        var rows = await QueryCoordinatorWorkAsync(
            nowUtc,
            options,
            new CoordinatorWorkFilter(),
            null,
            options.HomeExampleLimit,
            summary: true,
            cancellationToken);
        var counts = rows
            .GroupBy(x => x.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().CategoryCount, StringComparer.Ordinal);
        var totalCount = rows.Count == 0 ? 0 : rows[0].TotalCount;
        return new CoordinatorWorkSummaryDto(
            totalCount,
            counts,
            rows.OrderBy(x => x.SeverityRank)
                .ThenBy(x => x.CategoryRank)
                .ThenBy(x => x.DueAtUtc)
                .ThenBy(x => x.StableId)
                .Select(ToWorkItem)
                .ToArray());
    }

    public async Task<CoordinatorWorkPageDto> GetCoordinatorWorkPageAsync(
        DateTimeOffset nowUtc,
        CoordinatorAttentionOptions options,
        CoordinatorWorkFilter filter,
        CoordinatorWorkCursor? cursor,
        CancellationToken cancellationToken)
    {
        var rows = await QueryCoordinatorWorkAsync(
            nowUtc,
            options,
            filter,
            cursor,
            options.PageSize + 1,
            summary: false,
            cancellationToken);
        var hasNext = rows.Count > options.PageSize;
        var pageRows = rows.Take(options.PageSize).ToArray();
        if (cursor is { Forward: false })
        {
            pageRows = pageRows.Reverse().ToArray();
        }

        return new CoordinatorWorkPageDto(
            pageRows.Select(ToWorkItem).ToArray(),
            hasNext,
            cursor is not null);
    }

    public async Task<IReadOnlyList<VolunteerSearchResultDto>> SearchAssignableVolunteersAsync(
        string normalizedTerm,
        int limit,
        CancellationToken cancellationToken)
    {
        var term = normalizedTerm.Trim().ToUpperInvariant();
        var boundedLimit = Math.Clamp(limit, 1, 10);
        return await _dbContext.Volunteers
            .AsNoTracking()
            .Where(x => x.AnonymizedAtUtc == null &&
                        (x.NormalizedName.StartsWith(term) || x.NormalizedEmail.StartsWith(term)))
            .OrderBy(x => x.NormalizedEmail == term ? 0 : 1)
            .ThenBy(x => x.NormalizedName)
            .ThenBy(x => x.NormalizedEmail)
            .ThenBy(x => x.Id)
            .Take(boundedLimit)
            .Select(x => new VolunteerSearchResultDto(x.Id, x.Name, x.Email))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditHistoryQueryPage> GetAuditHistoryPageAsync(
        AuditHistoryFilter filter,
        AuditHistoryCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var boundedPageSize = Math.Clamp(pageSize, 1, 50);
        var rows = await QueryAuditHistoryAsync(filter, cursor, boundedPageSize + 1, cancellationToken);
        var hasNext = rows.Count > boundedPageSize;
        var pageRows = rows.Take(boundedPageSize).ToArray();
        if (cursor is { Forward: false })
        {
            pageRows = pageRows.Reverse().ToArray();
        }

        return new AuditHistoryQueryPage(pageRows, hasNext, cursor is not null);
    }

    public async Task<IReadOnlyList<string>> GetAuditActorsAsync(CancellationToken cancellationToken) =>
        await _dbContext.AuditEntries
            .AsNoTracking()
            .Select(x => x.Actor)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CoordinatorFilterChoiceDto>> SearchAuditShiftsAsync(
        string normalizedTerm,
        int limit,
        CancellationToken cancellationToken)
    {
        var term = normalizedTerm.Trim().ToUpperInvariant();
        var boundedLimit = Math.Clamp(limit, 1, 20);
        return await _dbContext.Shifts
            .AsNoTracking()
            .Where(x => x.Title.ToUpper().StartsWith(term))
            .OrderBy(x => x.Title)
            .ThenByDescending(x => x.StartsAtUtc)
            .ThenBy(x => x.Id)
            .Take(boundedLimit)
            .Select(x => new CoordinatorFilterChoiceDto(
                x.Id,
                x.Title))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CoordinatorAccessVerificationDto>> GetCoordinatorAccessVerificationsAsync(
        IReadOnlyCollection<string> normalizedEmails,
        CancellationToken cancellationToken)
    {
        if (normalizedEmails.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.AuditEntries
            .AsNoTracking()
            .Where(x => x.Action == "CoordinatorAccessVerified" && normalizedEmails.Contains(x.Actor.ToUpper()))
            .GroupBy(x => x.Actor.ToUpper())
            .Select(group => new
            {
                NormalizedEmail = group.Key,
                VerifiedAtUtc = group.Max(x => (DateTimeOffset?)x.OccurredAtUtc)
            })
            .ToListAsync(cancellationToken);
        var byEmail = rows.ToDictionary(x => x.NormalizedEmail, x => x.VerifiedAtUtc, StringComparer.Ordinal);
        return normalizedEmails
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new CoordinatorAccessVerificationDto(
                x,
                byEmail.TryGetValue(x, out var verifiedAtUtc) ? verifiedAtUtc : null))
            .ToArray();
    }

    private async Task<IReadOnlyList<CoordinatorWorkQueryRow>> QueryCoordinatorWorkAsync(
        DateTimeOffset nowUtc,
        CoordinatorAttentionOptions options,
        CoordinatorWorkFilter filter,
        CoordinatorWorkCursor? cursor,
        int limit,
        bool summary,
        CancellationToken cancellationToken)
    {
        var sql = CoordinatorWorkCte + (summary
            ? """
                , ranked AS (
                    SELECT work.*,
                           COUNT(*) OVER (PARTITION BY work."Category")::integer AS "CategoryCount",
                           COUNT(*) OVER ()::integer AS "TotalCount",
                           ROW_NUMBER() OVER (
                               PARTITION BY work."Category"
                               ORDER BY work."SeverityRank", work."CategoryRank", work."DueAtUtc", work."StableId") AS "RowNumber"
                    FROM work
                )
                SELECT "StableId", "Category", "CategoryRank", "SeverityRank", "DueAtUtc", "StartsAtUtc", "EndsAtUtc",
                       "ShiftId", "SlotId", "VolunteerId", "SeriesId", "CommitmentId", "Title", "Context", "PersonName",
                       "State", "RouteKind", "RouteId", "CategoryCount", "TotalCount", "RowNumber"
                FROM ranked
                WHERE "RowNumber" <= @example_limit
                ORDER BY "SeverityRank", "CategoryRank", "DueAtUtc", "StableId"
                """
            : $"""
                , ranked AS (
                    SELECT work.*,
                           COUNT(*) OVER (PARTITION BY work."Category")::integer AS "CategoryCount",
                           COUNT(*) OVER ()::integer AS "TotalCount",
                           ROW_NUMBER() OVER () AS "RowNumber"
                    FROM work
                )
                SELECT "StableId", "Category", "CategoryRank", "SeverityRank", "DueAtUtc", "StartsAtUtc", "EndsAtUtc",
                       "ShiftId", "SlotId", "VolunteerId", "SeriesId", "CommitmentId", "Title", "Context", "PersonName",
                       "State", "RouteKind", "RouteId", "CategoryCount", "TotalCount", "RowNumber"
                FROM ranked
                WHERE (COALESCE(@category, '') = '' OR "Category" = @category)
                  AND (@severity_rank < 0 OR "SeverityRank" = @severity_rank)
                  AND (
                      @cursor_set = FALSE OR
                      (@cursor_forward = TRUE AND ("SeverityRank", "CategoryRank", "DueAtUtc", "StableId") > (@cursor_severity, @cursor_category, @cursor_due, @cursor_id)) OR
                      (@cursor_forward = FALSE AND ("SeverityRank", "CategoryRank", "DueAtUtc", "StableId") < (@cursor_severity, @cursor_category, @cursor_due, @cursor_id))
                  )
                ORDER BY "SeverityRank" {(cursor is { Forward: false } ? "DESC" : "ASC")},
                         "CategoryRank" {(cursor is { Forward: false } ? "DESC" : "ASC")},
                         "DueAtUtc" {(cursor is { Forward: false } ? "DESC" : "ASC")},
                         "StableId" {(cursor is { Forward: false } ? "DESC" : "ASC")}
                LIMIT @page_limit
                """);
        var parameters = new List<NpgsqlParameter>
        {
            new("now", NpgsqlDbType.TimestampTz) { Value = nowUtc },
            new("urgent_until", NpgsqlDbType.TimestampTz) { Value = nowUtc.AddHours(options.UrgentHours) },
            new("soon_until", NpgsqlDbType.TimestampTz) { Value = nowUtc.AddHours(options.SoonHours) },
            new("group_zone", NpgsqlDbType.Text) { Value = await GetGroupTimeZoneIdForQueryAsync(cancellationToken) },
            new("category", NpgsqlDbType.Text) { Value = filter.NormalizedCategory ?? string.Empty },
            new("severity_rank", NpgsqlDbType.Integer) { Value = filter.SeverityRank ?? -1 },
            new("cursor_set", NpgsqlDbType.Boolean) { Value = cursor is not null },
            new("cursor_forward", NpgsqlDbType.Boolean) { Value = cursor?.Forward ?? true },
            new("cursor_severity", NpgsqlDbType.Integer) { Value = cursor?.SeverityRank ?? 0 },
            new("cursor_due", NpgsqlDbType.TimestampTz) { Value = cursor?.DueAtUtc ?? nowUtc },
            new("cursor_category", NpgsqlDbType.Integer) { Value = cursor?.CategoryRank ?? 0 },
            new("cursor_id", NpgsqlDbType.Uuid) { Value = cursor?.StableId ?? Guid.Empty },
            new("page_limit", NpgsqlDbType.Integer) { Value = Math.Clamp(limit, 1, 51) },
            new("example_limit", NpgsqlDbType.Integer) { Value = Math.Clamp(limit, 1, 3) }
        };
        return await _dbContext.CoordinatorWorkQueryRows
            .FromSqlRaw(sql, parameters.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AuditHistoryQueryRow>> QueryAuditHistoryAsync(
        AuditHistoryFilter filter,
        AuditHistoryCursor? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = AuditHistoryCte + $"""
            , filtered AS (
                SELECT *
                FROM history
                WHERE (COALESCE(@category, '') = '' OR "Category" = @category)
                  AND (
                      @cursor_set = FALSE OR
                      (@cursor_forward = TRUE AND ("OccurredAtUtc", "Id") < (@cursor_occurred, @cursor_id)) OR
                      (@cursor_forward = FALSE AND ("OccurredAtUtc", "Id") > (@cursor_occurred, @cursor_id))
                  )
            )
            SELECT "Id", "OccurredAtUtc", "Actor", "Action", "Category", "ShiftId", "VolunteerId", "ShiftTitle",
                   "ShiftStartsAtUtc", "VolunteerName", "VolunteerIsAnonymized"
            FROM filtered
            ORDER BY "OccurredAtUtc" {(cursor is { Forward: false } ? "ASC" : "DESC")},
                     "Id" {(cursor is { Forward: false } ? "ASC" : "DESC")}
            LIMIT @page_limit
            """;
        var parameters = new List<NpgsqlParameter>
        {
            new("from_utc", NpgsqlDbType.TimestampTz) { Value = filter.FromUtc ?? (object)DBNull.Value },
            new("through_utc", NpgsqlDbType.TimestampTz) { Value = filter.ThroughExclusiveUtc ?? (object)DBNull.Value },
            new("actor", NpgsqlDbType.Text) { Value = filter.NormalizedActor ?? string.Empty },
            new("shift_id", NpgsqlDbType.Uuid) { Value = filter.ShiftId ?? (object)DBNull.Value },
            new("volunteer_id", NpgsqlDbType.Uuid) { Value = filter.VolunteerId ?? (object)DBNull.Value },
            new("category", NpgsqlDbType.Text) { Value = filter.NormalizedCategory ?? string.Empty },
            new("cursor_set", NpgsqlDbType.Boolean) { Value = cursor is not null },
            new("cursor_forward", NpgsqlDbType.Boolean) { Value = cursor?.Forward ?? true },
            new("cursor_occurred", NpgsqlDbType.TimestampTz) { Value = cursor?.OccurredAtUtc ?? DateTimeOffset.UtcNow },
            new("cursor_id", NpgsqlDbType.Uuid) { Value = cursor?.AuditId ?? Guid.Empty },
            new("page_limit", NpgsqlDbType.Integer) { Value = Math.Clamp(limit, 1, 51) }
        };
        return await _dbContext.AuditHistoryQueryRows
            .FromSqlRaw(sql, parameters.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task<string> GetGroupTimeZoneIdForQueryAsync(CancellationToken cancellationToken) =>
        await _dbContext.GroupSettings
            .AsNoTracking()
            .Select(x => x.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken) ?? "Etc/UTC";

    private static CoordinatorWorkItemDto ToWorkItem(CoordinatorWorkQueryRow row) =>
        new(
            row.StableId,
            row.Category,
            row.CategoryRank,
            row.SeverityRank,
            row.DueAtUtc,
            row.StartsAtUtc,
            row.EndsAtUtc,
            row.ShiftId,
            row.SlotId,
            row.VolunteerId,
            row.SeriesId,
            row.CommitmentId,
            row.Title,
            row.Context,
            row.PersonName,
            row.State,
            row.RouteKind,
            row.RouteId);
    private static CoordinatorHomeExample ToHomeExample(CoordinatorWorkItemDto item) =>
        new(
            item.ShiftId.GetValueOrDefault(),
            item.SlotId,
            item.Title,
            item.Context,
            item.StartsAtUtc ?? item.DueAtUtc,
            item.EndsAtUtc ?? item.DueAtUtc,
            item.PersonName,
            item.DueAtUtc,
            item.Category == "message" ? item.State : null);
}
