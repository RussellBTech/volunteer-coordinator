using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue18AccessConcurrencyIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue18AccessConcurrencyIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApprovedRequestReassignmentRaceInvalidatesOldAccessAndPendingDelivery()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        Guid assignmentId;
        Guid oldVolunteerId;
        Guid slotId;
        string oldHub;
        string oldRecovery;

        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Approved reassignment race",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            var submission = await service.SubmitRequestAsync(
                slotId,
                "Original volunteer",
                "approved-race@example.org",
                null,
                default);
            oldHub = submission.StatusToken;
            assignmentId = (await service.ApproveRequestAsync(
                submission.RequestId,
                Coordinator,
                default)).AssignmentId;
            oldVolunteerId = await setupContext.Volunteers
                .Where(x => x.NormalizedEmail == "APPROVED-RACE@EXAMPLE.ORG")
                .Select(x => x.Id)
                .SingleAsync();

            var generated = new SecureTokenService().Generate();
            oldRecovery = generated.RawToken;
            setupContext.RecoveryTokens.Add(
                RecoveryToken.Create(oldVolunteerId, slotId, generated.Hash, Now));
            await setupContext.SaveChangesAsync();
        }

        await using var lockContext = _fixture.CreateContext();
        await using var lockTransaction = await lockContext.Database.BeginTransactionAsync();
        await new EfWorkflowStore(lockContext).LockSlotAsync(slotId, default);

        await using var reassignmentContext = _fixture.CreateContext();
        var reassignmentTask = ScheduleTestHelpers.CreateService(reassignmentContext, clock)
            .AssignDirectlyAsync(
                slotId,
                "Replacement volunteer",
                "replacement-race@example.org",
                null,
                Coordinator,
                default);
        await ScheduleTestHelpers.AssertBlockedAsync(reassignmentTask);

        await lockTransaction.CommitAsync();
        await reassignmentTask;

        await using var verification = _fixture.CreateContext();
        Assert.Equal(
            AssignmentStatus.Reassigned,
            await verification.Assignments
                .Where(x => x.Id == assignmentId)
                .Select(x => x.Status)
                .SingleAsync());
        Assert.Single(await verification.Assignments.Where(x => x.Status == AssignmentStatus.Assigned).ToListAsync());
        Assert.Empty(await verification.VolunteerAccessCapabilities
            .Where(x => x.VolunteerId == oldVolunteerId &&
                        x.ShiftSlotId == slotId &&
                        x.InvalidatedAtUtc == null)
            .ToListAsync());
        Assert.True(await verification.RecoveryTokens
            .Where(x => x.VolunteerId == oldVolunteerId && x.ShiftSlotId == slotId)
            .AllAsync(x => x.InvalidatedAtUtc.HasValue));
        Assert.All(
            await verification.NotificationIntents
                .Where(x => x.VolunteerId == oldVolunteerId && x.ShiftSlotId == slotId)
                .ToListAsync(),
            intent => Assert.Equal(NotificationIntentState.Cancelled, intent.State));

        await using var oldHubContext = _fixture.CreateContext();
        await Assert.ThrowsAsync<DomainException>(() =>
            ScheduleTestHelpers.CreateService(oldHubContext, clock)
                .InspectCommitmentHubAsync(oldHub, default));
        await using var oldRecoveryContext = _fixture.CreateContext();
        await Assert.ThrowsAsync<DomainException>(() =>
            ScheduleTestHelpers.CreateService(oldRecoveryContext, clock)
                .RedeemRecoveryAsync(oldRecovery, default));
    }

    [Fact]
    public async Task AccessFailureRetryAndReissueExposeDeliveryState()
    {
        await _fixture.ResetAsync();
        var clock = new MutableClock(Now);
        Guid assignmentId;
        Guid initialIntentId;
        Guid volunteerId;

        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Access delivery state",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            assignmentId = (await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Access state volunteer",
                "access-state@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            initialIntentId = await setupContext.NotificationIntents
                .Where(x => x.Kind == "AssignmentAccess")
                .Select(x => x.Id)
                .SingleAsync();
            volunteerId = await setupContext.Volunteers
                .Where(x => x.NormalizedEmail == "ACCESS-STATE@EXAMPLE.ORG")
                .Select(x => x.Id)
                .SingleAsync();
        }

        var fake = new FakeTransactionalEmailProvider();
        fake.EnqueueResult(new ProviderDeliveryResult(false, true, "Timeout"));
        fake.EnqueueResult(new ProviderDeliveryResult(false, true, "Timeout"));
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: fake);
        var worker = factory.Services.GetServices<IHostedService>()
            .OfType<NotificationDeliveryHostedService>()
            .Single();

        await worker.RunOnceAsync(default);
        await using (var retryContext = _fixture.CreateContext())
        {
            Assert.Equal(
                NotificationIntentState.RetryScheduled,
                await retryContext.NotificationIntents
                    .Where(x => x.Id == initialIntentId)
                    .Select(x => x.State)
                    .SingleAsync());
            var coverage = await ScheduleTestHelpers.CreateService(retryContext, clock)
                .GetCoverageAsync(default);
            Assert.Equal("Delivery pending", coverage.Single().Access?.State);
            Assert.Single(await retryContext.VolunteerAccessCapabilities
                .Where(x => x.VolunteerId == volunteerId &&
                            x.InvalidatedAtUtc == null)
                .ToListAsync());
        }

        clock.UtcNow = Now.AddMinutes(1);
        fake.EnqueueResult(new ProviderDeliveryResult(false, false, "InvalidRecipient"));
        await worker.RunOnceAsync(default);
        await using (var failedContext = _fixture.CreateContext())
        {
            Assert.Equal(
                NotificationIntentState.Failed,
                await failedContext.NotificationIntents
                    .Where(x => x.Id == initialIntentId)
                    .Select(x => x.State)
                    .SingleAsync());
            var coverage = await ScheduleTestHelpers.CreateService(failedContext, clock)
                .GetCoverageAsync(default);
            Assert.Equal("Message not sent", coverage.Single().Access?.State);
        }

        await using (var guardContext = _fixture.CreateContext())
        {
            await Assert.ThrowsAsync<DomainException>(() =>
                ScheduleTestHelpers.CreateService(guardContext, clock)
                    .RequestNotificationResendAsync(
                        initialIntentId,
                        Coordinator,
                        default));
        }

        await using (var reissueContext = _fixture.CreateContext())
        {
            await ScheduleTestHelpers.CreateService(reissueContext, clock)
                .RequestAccessReissueAsync(
                    assignmentId,
                    false,
                    Coordinator,
                    default);
            var coverage = await ScheduleTestHelpers.CreateService(reissueContext, clock)
                .GetCoverageAsync(default);
            Assert.Equal("Delivery pending", coverage.Single().Access?.State);
        }

        await worker.RunOnceAsync(default);
        await using var finalContext = _fixture.CreateContext();
        var finalCoverage = await ScheduleTestHelpers.CreateService(finalContext, clock)
            .GetCoverageAsync(default);
        Assert.Equal("Active", finalCoverage.Single().Access?.State);
        Assert.Single(await finalContext.VolunteerAccessCapabilities
            .Where(x => x.VolunteerId == volunteerId && x.InvalidatedAtUtc == null)
            .ToListAsync());
        Assert.Single(await finalContext.NotificationIntents
            .Where(x => x.Kind == "CoordinatorAccessReissue" &&
                        x.State == NotificationIntentState.Accepted)
            .ToListAsync());
    }


    [Fact]
    public async Task SeparateContextNormalReissueWaitsForSlotLockAndPreservesAccess()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        Guid assignmentId;
        Guid slotId;
        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Reissue race",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            assignmentId = (await service.AssignDirectlyAsync(
                slotId,
                "Reissue volunteer",
                "reissue-race@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
        }

        await using var lockContext = _fixture.CreateContext();
        await using var lockTransaction = await lockContext.Database.BeginTransactionAsync();
        await new EfWorkflowStore(lockContext).LockSlotAsync(slotId, default);

        await using var reissueContext = _fixture.CreateContext();
        var reissueTask = ScheduleTestHelpers.CreateService(reissueContext, clock)
            .RequestAccessReissueAsync(assignmentId, false, Coordinator, default);
        await ScheduleTestHelpers.AssertBlockedAsync(reissueTask);

        await lockTransaction.CommitAsync();
        await reissueTask;

        await using var verification = _fixture.CreateContext();
        Assert.Single(
            await verification.NotificationIntents
                .Where(x => x.Kind == "CoordinatorAccessReissue" && x.State == Domain.Notifications.NotificationIntentState.Pending)
                .ToListAsync());
        Assert.Contains(
            await verification.AuditEntries.ToListAsync(),
            x => x.Action == "VolunteerAccessReissueRequested" && x.EntityId == assignmentId);
    }

    [Fact]
    public async Task SeparateContextRevokeAndRecoveryRedemptionSerializeWithoutTwoActiveCapabilities()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        Guid volunteerId;
        Guid assignmentId;
        Guid slotId;
        string recoveryToken;
        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Revoke race",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            assignmentId = (await service.AssignDirectlyAsync(
                slotId,
                "Revoke volunteer",
                "revoke-race@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            volunteerId = await setupContext.Volunteers
                .Where(x => x.NormalizedEmail == "REVOKE-RACE@EXAMPLE.ORG")
                .Select(x => x.Id)
                .SingleAsync();
            var generated = new SecureTokenService().Generate();
            recoveryToken = generated.RawToken;
            setupContext.RecoveryTokens.Add(RecoveryToken.Create(volunteerId, slotId, generated.Hash, Now));
            await setupContext.SaveChangesAsync();
        }

        await using var lockContext = _fixture.CreateContext();
        await using var lockTransaction = await lockContext.Database.BeginTransactionAsync();
        await new EfWorkflowStore(lockContext).LockSlotAsync(slotId, default);

        await using var revokeContext = _fixture.CreateContext();
        await using var redeemContext = _fixture.CreateContext();
        var revokeTask = ScheduleTestHelpers.CreateService(revokeContext, clock)
            .RequestAccessReissueAsync(assignmentId, true, Coordinator, default);
        var redeemTask = ScheduleTestHelpers.CreateService(redeemContext, clock)
            .RedeemRecoveryAsync(recoveryToken, default);
        await ScheduleTestHelpers.AssertBlockedAsync(revokeTask);
        await ScheduleTestHelpers.AssertBlockedAsync(redeemTask);

        await lockTransaction.CommitAsync();
        await Task.WhenAll(
            revokeTask.ContinueWith(_ => { }),
            redeemTask.ContinueWith(_ => { }));

        await using var verification = _fixture.CreateContext();
        var activeCapabilities = await verification.VolunteerAccessCapabilities
            .Where(x => x.VolunteerId == volunteerId && x.InvalidatedAtUtc == null)
            .ToListAsync();
        Assert.InRange(activeCapabilities.Count, 0, 1);
        var recovery = await verification.RecoveryTokens.SingleAsync();
        Assert.True(recovery.UsedAtUtc.HasValue || recovery.InvalidatedAtUtc.HasValue);
        Assert.Contains(
            await verification.AuditEntries.ToListAsync(),
            x => x.Action == "VolunteerAccessRevokedByCoordinator" && x.EntityId == assignmentId);
    }

    [Fact]
    public async Task SeparateContextRevokeCancelsPendingAccessDeliveryAndCorrelatesAudit()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        Guid assignmentId;
        Guid pendingIntentId;
        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Pending revoke",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            assignmentId = (await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Pending volunteer",
                "pending-revoke@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            pendingIntentId = await setupContext.NotificationIntents
                .Where(x => x.Kind == "AssignmentAccess")
                .Select(x => x.Id)
                .SingleAsync();
        }

        await using (var revokeContext = _fixture.CreateContext())
        {
            await ScheduleTestHelpers.CreateService(revokeContext, clock)
                .RequestAccessReissueAsync(assignmentId, true, Coordinator, default);
        }

        await using var verification = _fixture.CreateContext();
        Assert.Equal(
            NotificationIntentState.Cancelled,
            await verification.NotificationIntents
                .Where(x => x.Id == pendingIntentId)
                .Select(x => x.State)
                .SingleAsync());
        var replacement = await verification.NotificationIntents
            .Where(x => x.Kind == "CoordinatorAccessReissue")
            .SingleAsync();
        Assert.Equal(NotificationIntentState.Pending, replacement.State);
        Assert.Contains(
            replacement.Id.ToString(),
            (await verification.AuditEntries
                .Where(x => x.Action == "VolunteerAccessReissueRequested")
                .Select(x => x.DetailJson)
                .SingleAsync()),
            StringComparison.OrdinalIgnoreCase);
    }
    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
