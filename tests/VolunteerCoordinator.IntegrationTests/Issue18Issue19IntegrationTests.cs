using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue18Issue19IntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue18Issue19IntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OneHubFollowsRequestAssignmentAndSingleUseActions()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Hub shift",
            "Community hall",
            null,
            Now.AddDays(2),
            Now.AddDays(2).AddHours(2),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = (await service.ListOpeningsAsync(default)).Single().SlotId;

        var submission = await service.SubmitRequestAsync(
            slotId,
            "Hub volunteer",
            "hub@example.org",
            null,
            default);
        var pending = await service.InspectCommitmentHubAsync(submission.StatusToken, default);
        Assert.Equal("Pending", pending.RequestStatus);
        Assert.Empty(pending.OfferedActions);

        await service.ApproveRequestAsync(submission.RequestId, Coordinator, default);
        var assigned = await service.InspectCommitmentHubAsync(submission.StatusToken, default);
        Assert.Equal("Assigned", assigned.AssignmentStatus);
        Assert.Equal(new[] { "Confirm", "Decline" }, assigned.OfferedActions);

        await service.ApplyHubActionAsync(submission.StatusToken, "Confirm", default);
        var confirmed = await service.InspectCommitmentHubAsync(submission.StatusToken, default);
        Assert.Equal("Confirmed", confirmed.AssignmentStatus);
        Assert.Equal(new[] { "Cancel" }, confirmed.OfferedActions);
        await Assert.ThrowsAsync<DomainException>(() =>
            service.ApplyHubActionAsync(submission.StatusToken, "Confirm", default));
        Assert.Equal(
            AssignmentStatus.Confirmed,
            await context.Assignments.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentHubActionsCommitOneWinner()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        string rawHub;
        await using (var setupContext = _fixture.CreateContext())
        {
            var setup = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                setup,
                "Concurrent hub shift",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await setup.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await setup.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            var slotId = (await setup.ListOpeningsAsync(default)).Single().SlotId;
            var submission = await setup.SubmitRequestAsync(slotId, "Race volunteer", "race-hub@example.org", null, default);
            await setup.ApproveRequestAsync(submission.RequestId, Coordinator, default);
            rawHub = submission.StatusToken;
        }

        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = ScheduleTestHelpers.CreateService(firstContext, clock);
        var second = ScheduleTestHelpers.CreateService(secondContext, clock);
        var firstTask = first.ApplyHubActionAsync(rawHub, "Confirm", default);
        var secondTask = second.ApplyHubActionAsync(rawHub, "Decline", default);
        var outcomes = await Task.WhenAll(
            firstTask.ContinueWith(task => task.Status == TaskStatus.RanToCompletion),
            secondTask.ContinueWith(task => task.Status == TaskStatus.RanToCompletion));

        Assert.Single(outcomes, outcome => outcome);
        await using var verification = _fixture.CreateContext();
        Assert.Contains(
            await verification.Assignments.Select(x => x.Status).ToListAsync(),
            status => status is AssignmentStatus.Confirmed or AssignmentStatus.Declined);
        Assert.Equal(
            1,
            await verification.AuditEntries.CountAsync(
                x => x.Action == "AssignmentConfirm" || x.Action == "AssignmentDecline"));
    }

    [Fact]
    public async Task RecoveryIsGenericAndRedemptionAtomicallyReplacesCapability()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Recovery shift",
            null,
            null,
            Now.AddDays(3),
            Now.AddDays(3).AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = (await service.ListOpeningsAsync(default)).Single().SlotId;
        var submission = await service.SubmitRequestAsync(slotId, "Recovery volunteer", "recover@example.org", null, default);
        var date = DateOnly.FromDateTime(Now.AddDays(3).UtcDateTime);

        var noMatch = await service.RequestRecoveryAsync("missing@example.org", date, default);
        var match = await service.RequestRecoveryAsync("RECOVER@example.org", date, default);
        Assert.Equal(noMatch.ReceiptMessage, match.ReceiptMessage);
        Assert.Single(await context.NotificationIntents.Where(x => x.Kind == "AccessRecovery").ToListAsync());

        var recoveryTokenService = new SecureTokenService();
        var rawRecovery = recoveryTokenService.Generate();
        var volunteerId = await context.Volunteers
            .Where(x => x.NormalizedEmail == "RECOVER@EXAMPLE.ORG")
            .Select(x => x.Id)
            .SingleAsync();
        context.RecoveryTokens.Add(RecoveryToken.Create(volunteerId, slotId, rawRecovery.Hash, Now));
        await context.SaveChangesAsync();

        var newHub = await service.RedeemRecoveryAsync(rawRecovery.RawToken, default);
        await Assert.ThrowsAsync<DomainException>(() =>
            service.InspectCommitmentHubAsync(submission.StatusToken, default));
        var recovered = await service.InspectCommitmentHubAsync(newHub, default);
        Assert.Equal("Pending", recovered.RequestStatus);
        Assert.Single(await context.VolunteerAccessCapabilities
            .Where(x => x.VolunteerId == volunteerId && x.ShiftSlotId == slotId && x.InvalidatedAtUtc == null)
            .ToListAsync());
    }

    [Fact]
    public async Task RecoverySkipsResolvedCommitmentsAndRejectsStaleRedemption()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        await using var context = _fixture.CreateContext();
        var service = ScheduleTestHelpers.CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Resolved recovery",
            null,
            null,
            Now.AddDays(3),
            Now.AddDays(3).AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = shift.Slots.Single().Id;
        var submission = await service.SubmitRequestAsync(
            slotId,
            "Resolved volunteer",
            "resolved-recovery@example.org",
            null,
            default);
        var volunteerId = await context.Volunteers
            .Where(x => x.NormalizedEmail == "RESOLVED-RECOVERY@EXAMPLE.ORG")
            .Select(x => x.Id)
            .SingleAsync();
        await service.RejectRequestAsync(submission.RequestId, Coordinator, default);

        var generic = await service.RequestRecoveryAsync(
            "resolved-recovery@example.org",
            DateOnly.FromDateTime(Now.AddDays(3).DateTime),
            default);
        Assert.Contains("If those details match", generic.ReceiptMessage, StringComparison.Ordinal);
        Assert.Empty(await context.NotificationIntents.Where(x => x.Kind == "AccessRecovery").ToListAsync());

        var generated = new SecureTokenService().Generate();
        context.RecoveryTokens.Add(RecoveryToken.Create(volunteerId, slotId, generated.Hash, Now));
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainException>(() =>
            service.RedeemRecoveryAsync(generated.RawToken, default));
        Assert.Null((await context.RecoveryTokens.SingleAsync()).UsedAtUtc);
    }

    [Fact]
    public async Task SignedResendWebhooksAreDeduplicatedAndMonotonic()
    {
        await _fixture.ResetAsync();
        var volunteerId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var volunteer = Volunteer.Create(
                "Webhook volunteer",
                "webhook@example.org",
                null,
                Now);
            seed.Volunteers.Add(volunteer);
            volunteerId = volunteer.Id;
            var intent = NotificationIntent.Create(
                "webhook-event",
                Guid.NewGuid(),
                volunteerId,
                null,
                "RequestReceipt",
                Now);
            intent.Accept(Now.AddSeconds(1), "provider-message-1");
            intentId = intent.Id;
            seed.NotificationIntents.Add(intent);
            await seed.SaveChangesAsync();
        }

        const string secret = "webhook-secret";
        var secretKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
        var body = "{\"type\":\"email.delivered\",\"created_at\":\"" +
            Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture) +
            "\",\"data\":{\"email_id\":\"provider-message-1\"}}";

        static string Sign(string secretValue, string id, string timestamp, string payload)
        {
            var key = Convert.FromBase64String(secretValue);
            var input = Encoding.UTF8.GetBytes($"{id}.{timestamp}.{payload}");
            return Convert.ToBase64String(HMACSHA256.HashData(key, input));
        }

        async Task<bool> PostAsync(string eventId, string eventType)
        {
            var payload = body.Replace("email.delivered", eventType, StringComparison.Ordinal);
            var timestamp = Now.ToUnixTimeSeconds().ToString();
            await using var webhookContext = _fixture.CreateContext();
            var processor = new ResendWebhookProcessor(
                webhookContext,
                new ScheduleTestHelpers.FixedClock(Now),
                Options.Create(new ResendOptions { WebhookSecret = secretKey }),
                Options.Create(new ResendWebhookOptions()));
            return await processor.ProcessAsync(
                payload,
                eventId,
                timestamp,
                $"v1,{Sign(secretKey, eventId, timestamp, payload)}",
                default);
        }

        Assert.True(await PostAsync("svix-1", "email.delivered"));
        var concurrentDuplicates = await Task.WhenAll(
            PostAsync("svix-concurrent", "email.delivered"),
            PostAsync("svix-concurrent", "email.delivered"));
        Assert.All(concurrentDuplicates, Assert.True);
        Assert.True(await PostAsync("svix-1", "email.delivered"));
        Assert.True(await PostAsync("svix-2", "email.bounced"));
        Assert.True(await PostAsync("svix-3", "email.delivered"));

        await using var verification = _fixture.CreateContext();
        Assert.Equal(
            NotificationIntentState.Bounced,
            await verification.NotificationIntents
                .Where(x => x.Id == intentId)
                .Select(x => x.State)
                .SingleAsync());
        Assert.Equal(4, await verification.ResendWebhookReceipts.CountAsync());
        Assert.Equal(volunteerId, await verification.NotificationIntents.Select(x => x.VolunteerId).SingleAsync());
    }

    [Fact]
    public void SafeTemplateEncodesHostileScheduleContent()
    {
        var template = new SafeEmailTemplateRenderer().Render(
            new VolunteerCoordinator.Application.Notifications.NotificationTemplateContext(
                "Volunteer <script>",
                "Shift & <",
                "Primary",
                Now,
                Now.AddHours(1),
                "Etc/UTC",
                "<img src=x>",
                "Do not <b>inject</b>",
                "Assigned",
                "https://example.test/Requests/Status/token",
                "reply@example.org"),
            "AssignmentAccess");

        Assert.DoesNotContain("<script>", template.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", template.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Shift &amp; &lt;", template.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Etc/UTC", template.TextBody, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("RequestReceipt")]
    [InlineData("AssignmentAccess")]
    [InlineData("AccessRecovery")]
    [InlineData("CoordinatorAccessReissue")]
    [InlineData("RequestDecision")]
    [InlineData("ScheduleCorrection")]
    [InlineData("Cancellation")]
    [InlineData("Deactivation")]
    [InlineData("VolunteerConfirm")]
    [InlineData("VolunteerDecline")]
    [InlineData("VolunteerCancel")]
    [InlineData("RecoveryRedeemed")]
    public void EveryNotificationEventRendersSafePlainAndHtmlTemplates(string kind)
    {
        var template = new SafeEmailTemplateRenderer().Render(
            new VolunteerCoordinator.Application.Notifications.NotificationTemplateContext(
                "Volunteer",
                "Shift",
                "Primary",
                Now,
                Now.AddHours(1),
                "Etc/UTC",
                "Local hall",
                "Use the north entrance.",
                "Assigned",
                kind is "RequestReceipt" or "AssignmentAccess"
                    ? "https://example.test/Requests/Status/opaque"
                    : null,
                "reply@example.org"),
            kind);

        Assert.NotEmpty(template.Subject);
        Assert.NotEmpty(template.TextBody);
        Assert.NotEmpty(template.HtmlBody);
        Assert.DoesNotContain("<script", template.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reply@example.org", template.HtmlBody, StringComparison.Ordinal);
    }
}
