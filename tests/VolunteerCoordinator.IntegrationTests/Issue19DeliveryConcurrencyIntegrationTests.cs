using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Infrastructure.Notifications;
using Xunit;
using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Infrastructure.Security;


namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue19DeliveryConcurrencyIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue19DeliveryConcurrencyIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ForcedTransientFailuresFollowAbsoluteScheduleAndFinalFailure()
    {
        await _fixture.ResetAsync();
        var clock = new MutableClock(Start);
        Guid intentId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Retry schedule",
                null,
                null,
                Start.AddDays(2),
                Start.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Retry volunteer",
                "retry@example.org",
                null,
                Coordinator,
                default);
            intentId = await context.NotificationIntents.Select(x => x.Id).SingleAsync();
        }

        var fake = new FakeTransactionalEmailProvider();
        for (var i = 0; i < 10; i++)
        {
            fake.EnqueueResult(new ProviderDeliveryResult(false, true, "Timeout"));
        }

        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: fake);
        var worker = factory.Services.GetServices<IHostedService>().OfType<NotificationDeliveryHostedService>().Single();
        foreach (var offset in new[] { 0, 1, 5, 30, 120 })
        {
            clock.UtcNow = Start.AddMinutes(offset);
            await worker.RunOnceAsync(default);
        }

        await using var verification = _fixture.CreateContext();
        var intent = await verification.NotificationIntents.SingleAsync(x => x.Id == intentId);
        Assert.Equal(NotificationIntentState.Failed, intent.State);
        Assert.Equal(5, intent.AttemptCount);
        Assert.Equal("Timeout", intent.FailureCategory);
        Assert.Equal(5, await verification.NotificationDeliveryAttempts.CountAsync(x => x.NotificationIntentId == intentId));
    }

    [Fact]
    public async Task ExpiredLeaseIsAbandonedAndReclaimedWithFreshAttempt()
    {
        await _fixture.ResetAsync();
        var clock = new MutableClock(Start);
        Guid intentId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Lease recovery",
                null,
                null,
                Start.AddDays(2),
                Start.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Lease volunteer",
                "lease@example.org",
                null,
                Coordinator,
                default);
            var intent = await context.NotificationIntents.SingleAsync();
            intentId = intent.Id;
            intent.Claim(Start, Start.AddMinutes(2));
            context.NotificationDeliveryAttempts.Add(
                NotificationDeliveryAttempt.Create(intent.Id, 1, Start));
            await context.SaveChangesAsync();
        }

        var fake = new FakeTransactionalEmailProvider();
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: fake);
        clock.UtcNow = Start.AddMinutes(3);
        var worker = factory.Services.GetServices<IHostedService>().OfType<NotificationDeliveryHostedService>().Single();
        await worker.RunOnceAsync(default);

        await using var verification = _fixture.CreateContext();
        var attempts = await verification.NotificationDeliveryAttempts
            .Where(x => x.NotificationIntentId == intentId)
            .OrderBy(x => x.Ordinal)
            .ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal("LeaseAbandoned", attempts[0].OutcomeCategory);
        Assert.Equal("Accepted", attempts[1].OutcomeCategory);
        Assert.Equal(NotificationIntentState.Accepted, await verification.NotificationIntents
            .Where(x => x.Id == intentId)
            .Select(x => x.State)
            .SingleAsync());
    }

    [Fact]
    public async Task StaleLeaseCompletionCannotInvalidateNewerRecoveryToken()
    {
        await _fixture.ResetAsync();
        var clock = new MutableClock(Start);
        Guid intentId;
        Guid volunteerId;
        Guid slotId;

        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Recovery lease race",
                null,
                null,
                Start.AddDays(2),
                Start.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            await service.SubmitRequestAsync(
                slotId,
                "Recovery lease volunteer",
                "recovery-lease@example.org",
                null,
                default);
            volunteerId = await setupContext.Volunteers
                .Where(x => x.NormalizedEmail == "RECOVERY-LEASE@EXAMPLE.ORG")
                .Select(x => x.Id)
                .SingleAsync();

            var receipt = await setupContext.NotificationIntents
                .SingleAsync(x => x.Kind == "RequestReceipt");
            receipt.Cancel(Start, "TestSetup");
            var intent = NotificationIntent.Create(
                "lease-recovery-race",
                Guid.NewGuid(),
                volunteerId,
                slotId,
                "AccessRecovery",
                Start);
            setupContext.NotificationIntents.Add(intent);
            intentId = intent.Id;
            await setupContext.SaveChangesAsync();
        }

        var firstProvider = new BlockingProvider();
        using var firstFactory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: firstProvider);
        var firstWorker = firstFactory.Services.GetServices<IHostedService>()
            .OfType<NotificationDeliveryHostedService>()
            .Single();
        var firstRun = firstWorker.RunOnceAsync(default);
        await firstProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var afterFirstClaim = _fixture.CreateContext())
        {
            Assert.Equal(
                NotificationIntentState.InFlight,
                await afterFirstClaim.NotificationIntents
                    .Where(x => x.Id == intentId)
                    .Select(x => x.State)
                    .SingleAsync());
            Assert.Single(await afterFirstClaim.RecoveryTokens
                .Where(x => x.VolunteerId == volunteerId && x.ShiftSlotId == slotId)
                .ToListAsync());
        }

        clock.UtcNow = Start.AddMinutes(3);
        var secondProvider = new FakeTransactionalEmailProvider();
        using var secondFactory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: secondProvider);
        var secondWorker = secondFactory.Services.GetServices<IHostedService>()
            .OfType<NotificationDeliveryHostedService>()
            .Single();
        await secondWorker.RunOnceAsync(default);

        await using (var afterSecondClaim = _fixture.CreateContext())
        {
            Assert.Equal(
                NotificationIntentState.Accepted,
                await afterSecondClaim.NotificationIntents
                    .Where(x => x.Id == intentId)
                    .Select(x => x.State)
                    .SingleAsync());
            var tokens = await afterSecondClaim.RecoveryTokens
                .Where(x => x.VolunteerId == volunteerId && x.ShiftSlotId == slotId)
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync();
            Assert.Equal(2, tokens.Count);
            Assert.NotNull(tokens[0].InvalidatedAtUtc);
            Assert.Null(tokens[1].InvalidatedAtUtc);
            Assert.Equal(
                tokens[1].Id,
                await afterSecondClaim.NotificationIntents
                    .Where(x => x.Id == intentId)
                    .Select(x => x.RecoveryTokenId)
                    .SingleAsync());
        }

        firstProvider.Release.TrySetResult(null);
        await firstRun;

        await using var verification = _fixture.CreateContext();
        var finalTokens = await verification.RecoveryTokens
            .Where(x => x.VolunteerId == volunteerId && x.ShiftSlotId == slotId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        Assert.Equal(2, finalTokens.Count);
        Assert.NotNull(finalTokens[0].InvalidatedAtUtc);
        Assert.Null(finalTokens[1].InvalidatedAtUtc);
        Assert.Equal(
            finalTokens[1].Id,
            await verification.NotificationIntents
                .Where(x => x.Id == intentId)
                .Select(x => x.RecoveryTokenId)
                .SingleAsync());
    }

    [Fact]
    public async Task OrdinaryResendLocksSourceAndUsesDeterministicPendingKey()
    {
        await _fixture.ResetAsync();
        var clock = new MutableClock(Start);
        Guid sourceIntentId;
        Guid volunteerId;
        Guid slotId;

        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Ordinary resend",
                null,
                null,
                Start.AddDays(2),
                Start.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
            var submission = await service.SubmitRequestAsync(
                slotId,
                "Ordinary resend volunteer",
                "ordinary-resend@example.org",
                null,
                default);
            volunteerId = await setupContext.Volunteers
                .Where(x => x.NormalizedEmail == "ORDINARY-RESEND@EXAMPLE.ORG")
                .Select(x => x.Id)
                .SingleAsync();
            var source = NotificationIntent.Create(
                "ordinary-source",
                submission.RequestId,
                volunteerId,
                slotId,
                "RequestDecision",
                Start);
            source.Fail(Start.AddSeconds(1), "PermanentFailure");
            setupContext.NotificationIntents.Add(source);
            sourceIntentId = source.Id;
            await setupContext.SaveChangesAsync();
        }

        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = ScheduleTestHelpers.CreateService(firstContext, clock)
            .RequestNotificationResendAsync(sourceIntentId, Coordinator, default);
        var second = ScheduleTestHelpers.CreateService(secondContext, clock)
            .RequestNotificationResendAsync(sourceIntentId, Coordinator, default);
        var outcomes = await Task.WhenAll(
            first.ContinueWith(task => task.Status == TaskStatus.RanToCompletion),
            second.ContinueWith(task => task.Status == TaskStatus.RanToCompletion));

        Assert.Single(outcomes, outcome => outcome);
        await using var verification = _fixture.CreateContext();
        var resendIntents = await verification.NotificationIntents
            .Where(x => x.EventKey == $"resend:{sourceIntentId:N}")
            .ToListAsync();
        var resend = Assert.Single(resendIntents);
        Assert.Equal(NotificationIntentState.Pending, resend.State);
        Assert.Single(await verification.AuditEntries
            .Where(x => x.Action == "NotificationResendRequested" &&
                        x.EntityId == sourceIntentId)
            .ToListAsync());
    }




    [Fact]
    public async Task ProviderRetryAfterCannotExceedApprovedAbsoluteOffset()
    {
        await _fixture.ResetAsync();
        var clock = new MutableClock(Start);
        Guid intentId;
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Retry-After bound",
                null,
                null,
                Start.AddDays(2),
                Start.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Retry-After volunteer",
                "retry-after@example.org",
                null,
                Coordinator,
                default);
            intentId = await context.NotificationIntents.Select(x => x.Id).SingleAsync();
        }

        var fake = new FakeTransactionalEmailProvider();
        fake.EnqueueResult(new ProviderDeliveryResult(
            false,
            true,
            "RateLimited",
            RetryAfterUtc: Start.AddDays(30)));
        fake.EnqueueResult(new ProviderDeliveryResult(
            false,
            true,
            "RateLimited",
            RetryAfterUtc: Start.AddDays(30)));
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: fake);
        var worker = factory.Services.GetServices<IHostedService>()
            .OfType<NotificationDeliveryHostedService>()
            .Single();
        await worker.RunOnceAsync(default);

        await using var verification = _fixture.CreateContext();
        var intent = await verification.NotificationIntents.SingleAsync(x => x.Id == intentId);
        Assert.Equal(NotificationIntentState.RetryScheduled, intent.State);
        Assert.Equal(Start.AddMinutes(1), intent.NextAttemptAtUtc);
    }


    [Fact]
    public async Task LateProviderResultCannotOverwriteCancellation()
    {
        await _fixture.ResetAsync();
        var clock = new MutableClock(Start);
        Guid assignmentId;
        Guid intentId;
        await using (var setupContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(setupContext, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Late provider cancellation",
                null,
                null,
                Start.AddDays(2),
                Start.AddDays(2).AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            assignmentId = (await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Late provider volunteer",
                "late-provider@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            intentId = await setupContext.NotificationIntents
                .Where(x => x.Kind == "AssignmentAccess")
                .Select(x => x.Id)
                .SingleAsync();
        }

        var provider = new BlockingProvider();
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            emailProvider: provider);
        var worker = factory.Services.GetServices<IHostedService>()
            .OfType<NotificationDeliveryHostedService>()
            .Single();
        var workerTask = worker.RunOnceAsync(default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var cancellationContext = _fixture.CreateContext())
        {
            await ScheduleTestHelpers.CreateService(cancellationContext, clock)
                .CancelAssignmentAsync(assignmentId, Coordinator, default);
        }

        provider.Release.TrySetResult(null);
        await workerTask;

        await using var verification = _fixture.CreateContext();
        Assert.Equal(
            NotificationIntentState.Cancelled,
            await verification.NotificationIntents
                .Where(x => x.Id == intentId)
                .Select(x => x.State)
                .SingleAsync());
        Assert.Equal(
            "ClaimCancelled",
            await verification.NotificationDeliveryAttempts
                .Where(x => x.NotificationIntentId == intentId)
                .Select(x => x.OutcomeCategory)
                .SingleAsync());
    }

    private sealed class BlockingProvider : ITransactionalEmailProvider
    {
        public TaskCompletionSource<object?> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProviderDeliveryResult> SendAsync(
            string recipient,
            string from,
            string replyTo,
            string idempotencyKey,
            EmailTemplate template,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(null);
            await Release.Task.WaitAsync(cancellationToken);
            return new ProviderDeliveryResult(true, false, "Accepted", "late-provider-message");
        }
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
