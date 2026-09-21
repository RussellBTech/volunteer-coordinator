using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue22PerformanceIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue22PerformanceIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ScaledCoordinatorPagesKeepFixedCommandBudgets()
    {
        await _fixture.ResetAsync();
        await SeedScaledFixtureAsync();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        counts["Home"] = await MeasureAsync(async (service, _) =>
        {
            await service.GetCoordinatorHomeAsync(default);
        });
        counts["Work"] = await MeasureAsync(async (service, _) =>
        {
            await service.GetCoordinatorWorkPageAsync(
                new Application.Models.CoordinatorWorkFilter(),
                null,
                null,
                default);
        });
        counts["Requests"] = await MeasureAsync(async (service, context) =>
        {
            await service.ListRequestsAsync(default);
            var store = new EfWorkflowStore(context);
            var recurring = new RecurringCommitmentService(
                store,
                store,
                store,
                new ScheduleTestHelpers.FixedClock(Now),
                new SecureTokenService());
            await recurring.ListRequestsAsync(default);
        });
        counts["Coverage"] = await MeasureAsync(async (service, _) =>
        {
            await service.GetCoverageAsync(default);
        });
        counts["Messages"] = await MeasureAsync(async (service, _) =>
        {
            await service.GetActionableMessagesPageAsync(1, default);
            await service.ListNotificationIntentsAsync(default);
        });
        counts["History"] = await MeasureAsync(async (service, _) =>
        {
            await service.GetAuditActorsAsync(default);
            await service.GetAuditHistoryPageAsync(
                new Application.Models.AuditHistoryFilter(),
                null,
                default);
        });
        counts["Search"] = await MeasureVolunteerSearchPageAsync();

        Assert.InRange(counts["Home"], 1, 6);
        Assert.InRange(counts["Work"], 1, 6);
        Assert.InRange(counts["Requests"], 1, 5);
        Assert.InRange(counts["Coverage"], 1, 5);
        Assert.InRange(counts["Messages"], 1, 5);
        Assert.InRange(counts["History"], 1, 6);
        Assert.Equal(1, counts["Search"]);

        await AssertIndexedPlansAsync();
    }
    private async Task<int> MeasureVolunteerSearchPageAsync()
    {
        Guid slotId;
        await using (var lookup = _fixture.CreateContext())
        {
            slotId = await lookup.ShiftSlots
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .FirstAsync();
        }

        var capture = new SqlCommandCaptureInterceptor();
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: new ScheduleTestHelpers.FixedClock(Now),
            commandInterceptor: capture);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var login = await client.GetAsync("/development/login");
        var loginHtml = await login.Content.ReadAsStringAsync();
        var loginToken = Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await client.PostAsync(
            "/development/login",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", loginToken),
                new KeyValuePair<string, string>("email", Coordinator)
            ]));

        var page = await client.GetAsync($"/Coordinator/Assignments/Assign/{slotId}?mode=known");
        var pageHtml = await page.Content.ReadAsStringAsync();
        var requestToken = Regex.Match(
            pageHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        capture.Reset();

        var response = await client.PostAsync(
            $"/Coordinator/Assignments/Assign/{slotId}?handler=Search",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", requestToken),
                new KeyValuePair<string, string>("Mode", "known"),
                new KeyValuePair<string, string>("SearchTerm", "VOLUNTEER 000")
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return capture.Count;
    }

    private async Task<int> MeasureAsync(
        Func<VolunteerCoordinatorService, VolunteerCoordinatorDbContext, Task> operation)
    {
        var capture = new SqlCommandCaptureInterceptor();
        await using var context = _fixture.CreateContext(null, capture);
        var service = ScheduleTestHelpers.CreateService(
            context,
            new ScheduleTestHelpers.FixedClock(Now));
        await operation(service, context);
        return capture.Count;
    }

    private async Task SeedScaledFixtureAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "GroupSettings" ("Id", "TimeZoneId")
            VALUES ('00000000-0000-0000-0000-000000000001', 'Etc/UTC');

            INSERT INTO "Volunteers" ("Id", "Name", "Email", "NormalizedEmail", "NormalizedName", "Phone", "CreatedAtUtc", "UpdatedAtUtc", "AnonymizedAtUtc")
            SELECT md5('issue22-volunteer-' || i)::uuid,
                   'Volunteer ' || lpad(i::text, 4, '0'),
                   'volunteer-' || lpad(i::text, 4, '0') || '@example.org',
                   upper('volunteer-' || lpad(i::text, 4, '0') || '@example.org'),
                   upper('volunteer ' || lpad(i::text, 4, '0')),
                   NULL,
                   '2025-12-01T00:00:00Z',
                   '2025-12-01T00:00:00Z',
                   NULL
            FROM generate_series(1, 1000) AS series(i);

            INSERT INTO "Shifts" ("Id", "Title", "Location", "Notes", "StartsAtUtc", "EndsAtUtc", "IsActive", "PublishedAtUtc", "UpdatedAtUtc", "SignupPolicy")
            SELECT md5('issue22-shift-' || i)::uuid,
                   'Shift ' || lpad(i::text, 5, '0'),
                   'Community hall',
                   NULL,
                   '2026-01-02T12:00:00Z'::timestamptz + (i % 72) * interval '1 hour',
                   '2026-01-02T13:00:00Z'::timestamptz + (i % 72) * interval '1 hour',
                   TRUE,
                   '2026-01-01T00:00:00Z',
                   '2026-01-01T00:00:00Z',
                   0
            FROM generate_series(1, 5000) AS series(i);

            INSERT INTO "ShiftSlots" ("Id", "ShiftId", "Kind", "Position", "IsActive")
            SELECT md5('issue22-slot-' || i)::uuid,
                   md5('issue22-shift-' || i)::uuid,
                   0,
                   1,
                   TRUE
            FROM generate_series(1, 5000) AS series(i);

            INSERT INTO "ShiftRequests" ("Id", "ShiftSlotId", "VolunteerId", "Status", "RequestedAtUtc", "ResolvedAtUtc", "ResolvedByCoordinatorEmail")
            SELECT md5('issue22-request-' || i)::uuid,
                   md5('issue22-slot-' || i)::uuid,
                   md5('issue22-volunteer-' || i)::uuid,
                   0,
                   '2026-01-01T09:00:00Z'::timestamptz + i * interval '1 minute',
                   NULL,
                   NULL
            FROM generate_series(1, 50) AS series(i);

            INSERT INTO "NotificationAttempts" ("Id", "TransitionId", "Kind", "State", "CreatedAtUtc", "CompletedAtUtc", "ErrorSummary")
            SELECT md5('issue22-attempt-' || i)::uuid,
                   md5('issue22-request-' || i)::uuid,
                   'RequestReceived',
                   2,
                   '2026-01-01T09:30:00Z',
                   '2026-01-01T09:31:00Z',
                   'safe fixture failure'
            FROM generate_series(1, 50) AS series(i);
            INSERT INTO "NotificationIntents" ("Id", "EventKey", "TransitionId", "VolunteerId", "ShiftSlotId", "Kind", "State", "CreatedAtUtc", "NextAttemptAtUtc", "AttemptCount", "FailureCategory")
            SELECT md5('issue22-intent-' || i)::uuid,
                   'issue22-intent-' || i,
                   md5('issue22-request-' || i)::uuid,
                   md5('issue22-volunteer-' || i)::uuid,
                   md5('issue22-slot-' || i)::uuid,
                   'RequestReceipt',
                   7,
                   '2026-01-01T09:30:00Z',
                   '2026-01-01T09:30:00Z',
                   1,
                   'fixture failure'
            FROM generate_series(1, 50) AS series(i);

            INSERT INTO "AuditEntries" ("Id", "OccurredAtUtc", "Actor", "Action", "EntityKind", "EntityId", "DetailJson", "ShiftId", "VolunteerId")
            SELECT md5('issue22-audit-' || i)::uuid,
                   '2026-01-01T00:00:00Z'::timestamptz + i * interval '1 second',
                   CASE WHEN i % 2 = 0 THEN 'COORDINATOR@EXAMPLE.ORG' ELSE 'SECOND@EXAMPLE.ORG' END,
                   CASE WHEN i % 3 = 0 THEN 'ShiftPublished' ELSE 'RequestSubmitted' END,
                   CASE WHEN i % 3 = 0 THEN 'Shift' ELSE 'ShiftRequest' END,
                   md5('issue22-shift-' || ((i - 1) % 5000 + 1))::uuid,
                   jsonb_build_object(),
                   md5('issue22-shift-' || ((i - 1) % 5000 + 1))::uuid,
                   md5('issue22-volunteer-' || ((i - 1) % 1000 + 1))::uuid
            FROM generate_series(1, 10000) AS series(i);
            """);
    }

    private async Task AssertIndexedPlansAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE tablename IN ('Volunteers', 'AuditEntries')
              AND indexname IN (
                  'IX_Volunteers_NormalizedName',
                  'IX_Volunteers_NormalizedName_Active',
                  'IX_AuditEntries_OccurredAtUtc_Id',
                  'IX_AuditEntries_Actor_OccurredAtUtc_Id',
                  'IX_AuditEntries_Action_OccurredAtUtc_Id');
            """;
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }
        await reader.DisposeAsync();

        Assert.Contains("IX_Volunteers_NormalizedName", indexes);
        Assert.Contains("IX_AuditEntries_OccurredAtUtc_Id", indexes);
        Assert.Contains("IX_AuditEntries_Actor_OccurredAtUtc_Id", indexes);
        Assert.Contains("IX_AuditEntries_Action_OccurredAtUtc_Id", indexes);
        await using (var setting = connection.CreateCommand())
        {
            setting.CommandText = "SET enable_seqscan = off;";
            await setting.ExecuteNonQueryAsync();
        }

        var searchPlan = await ReadPlanAsync(
            connection,
            """
            EXPLAIN (FORMAT TEXT)
            SELECT "Id"
            FROM "Volunteers"
            WHERE "AnonymizedAtUtc" IS NULL
              AND "NormalizedName" = 'VOLUNTEER 0001'
            ORDER BY "NormalizedName", "Id"
            LIMIT 10;
            """);
        var historyPlan = await ReadPlanAsync(
            connection,
            """
            EXPLAIN (FORMAT TEXT)
            SELECT "Id"
            FROM "AuditEntries"
            WHERE "OccurredAtUtc" < '2026-01-02T00:00:00Z'
            ORDER BY "OccurredAtUtc" DESC, "Id" DESC
            LIMIT 51;
            """);
        Assert.Contains("Index Scan", searchPlan, StringComparison.Ordinal);
        Assert.True(
            historyPlan.Contains("IX_AuditEntries_OccurredAtUtc_Id", StringComparison.Ordinal),
            historyPlan);

        static async Task<string> ReadPlanAsync(NpgsqlConnection connection, string sql)
        {
            await using var planCommand = connection.CreateCommand();
            planCommand.CommandText = sql;
            await using var planReader = await planCommand.ExecuteReaderAsync();
            var lines = new List<string>();
            while (await planReader.ReadAsync())
            {
                lines.Add(planReader.GetString(0));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
