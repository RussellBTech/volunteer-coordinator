using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue19SecretScanIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue19SecretScanIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DatabaseRowsAndSchemaContainNoRawDeliveryMaterial()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        Guid slotId;
        string rawHub;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Secret scan",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            rawHub = (await service.SubmitRequestAsync(
                slotId,
                "Scan volunteer",
                "scan@example.org",
                null,
                default)).StatusToken;
        }

        var fake = new FakeTransactionalEmailProvider();
        var logs = new CapturingLoggerProvider();
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: fake,
            loggerProvider: logs);
        await using var scope = factory.Services.CreateAsyncScope();
        var serviceInFactory = scope.ServiceProvider.GetRequiredService<VolunteerCoordinatorService>();
        await serviceInFactory.RequestRecoveryAsync("scan@example.org", DateOnly.FromDateTime(Now.AddDays(2).DateTime), default);
        var worker = factory.Services.GetServices<IHostedService>().OfType<NotificationDeliveryHostedService>().Single();
        await worker.RunOnceAsync(default);

        var recoveryMessages = fake.Messages
            .Where(message => message.Template.TextBody.Contains(
                "/Commitments/Recover/",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, recoveryMessages.Length);
        var recoveryMessage = recoveryMessages[0];
        var recoveryUrl = recoveryMessage.Template.TextBody
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.Contains("/Commitments/Recover/", StringComparison.Ordinal));

        await using var verification = _fixture.CreateContext();
        var tables = new[]
        {
            "NotificationIntents",
            "NotificationDeliveryAttempts",
            "ResendWebhookReceipts",
            "NotificationAttempts",
            "ShiftRequests"
        };
        var columns = await verification.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name IN ('NotificationIntents', 'NotificationDeliveryAttempts', 'ResendWebhookReceipts', 'NotificationAttempts', 'ShiftRequests')
                """)
            .ToListAsync();
        var forbiddenColumns = new[]
        {
            "Destination", "Body", "RawToken", "RecoveryUrl", "HubUrl", "Recipient", "Payload", "ApiKey", "WebhookSecret"
        };
        Assert.DoesNotContain(columns, column => forbiddenColumns.Contains(column, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("StatusTokenHash", columns, StringComparer.Ordinal);
        Assert.DoesNotContain("StatusTokenExpiresAtUtc", columns, StringComparer.Ordinal);
        Assert.DoesNotContain("StatusTokenInvalidatedAtUtc", columns, StringComparer.Ordinal);

        var persistedText = new List<string>();
        persistedText.AddRange(await verification.NotificationIntents.Select(x => JsonSerializer.Serialize(x)).ToListAsync());
        persistedText.AddRange(await verification.NotificationDeliveryAttempts.Select(x => JsonSerializer.Serialize(x)).ToListAsync());
        persistedText.AddRange(await verification.ResendWebhookReceipts.Select(x => JsonSerializer.Serialize(x)).ToListAsync());
        persistedText.AddRange(await verification.NotificationAttempts.Select(x => JsonSerializer.Serialize(x)).ToListAsync());
        persistedText.AddRange(await verification.AuditEntries.Select(x => x.DetailJson).ToListAsync());
        Assert.DoesNotContain(persistedText, value => value.Contains(rawHub, StringComparison.Ordinal));
        Assert.DoesNotContain(persistedText, value => value.Contains(recoveryUrl, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, value => value.Contains(rawHub, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, value => value.Contains(recoveryUrl, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, value => value.Contains("scan@example.org", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.Entries, value => value.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(persistedText, value => value.Contains("scan@example.org", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(persistedText, value => value.Contains("resend", StringComparison.OrdinalIgnoreCase) && value.Contains("key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LegacyNotificationMigrationDropsDestinationAndOriginalBytes()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var columns = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'NotificationAttempts'
                  AND column_name = 'Destination'
                """)
            .ToListAsync();
        Assert.Empty(columns);

        var attempt = VolunteerCoordinator.Domain.Notifications.NotificationAttempt.Create(
            Guid.NewGuid(),
            "Legacy",
            "legacy-original@example.org",
            Now);
        attempt.Fail(Now, "safe failure");
        context.NotificationAttempts.Add(attempt);
        await context.SaveChangesAsync();
        Assert.DoesNotContain(
            "legacy-original@example.org",
            JsonSerializer.Serialize(await context.NotificationAttempts.ToListAsync()),
            StringComparison.OrdinalIgnoreCase);
    }
}
