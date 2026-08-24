using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using VolunteerCoordinator.Infrastructure.Time;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class WorkflowIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public WorkflowIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompleteWorkflowKeepsNotificationFailureSeparateAndReopensEndedAssignments()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await service.CreateShiftAsync("Food service", "Community hall", "Bring badge", starts, starts.AddHours(2), 1, Coordinator, default);

        Assert.Empty(await service.ListOpeningsAsync(default));
        var version = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Version;
        await service.PublishShiftAsync(shiftId, version, Coordinator, default);
        var opening = Assert.Single(await service.ListOpeningsAsync(default), x => x.SlotLabel == "Primary");

        var submission = await service.SubmitRequestAsync(opening.SlotId, "Alex Rivera", "Alex@example.org", null, default);
        Assert.NotNull(submission.NotificationWarning);
        Assert.Equal(RequestStatus.Pending, (await context.ShiftRequests.SingleAsync()).Status);
        Assert.Equal(NotificationState.Failed, (await context.NotificationAttempts.SingleAsync()).State);
        Assert.NotEmpty(await context.AuditEntries.ToListAsync());

        await Assert.ThrowsAsync<DomainException>(() =>
            service.SubmitRequestAsync(opening.SlotId, "Alex Rivera", "alex@example.org", null, default));
        Assert.Single(await context.ShiftRequests.ToListAsync());

        var status = await service.GetRequestStatusAsync(submission.StatusToken, default);
        Assert.Equal("Pending", status.RequestStatus);
        var approved = await service.ApproveRequestAsync(submission.RequestId, Coordinator, default);
        Assert.NotNull(approved.NotificationWarning);
        Assert.DoesNotContain(await service.ListOpeningsAsync(default), item => item.SlotId == opening.SlotId);

        var links = await service.GenerateActionLinksAsync(approved.AssignmentId, Coordinator, default);
        var storedTokens = await context.ActionTokens.ToListAsync();
        Assert.Equal(3, storedTokens.Count);
        Assert.All(storedTokens, token => Assert.Equal(32, token.TokenHash.Length));
        var confirmation = await service.ApplyActionAsync(links.ConfirmToken!, default);
        Assert.Equal("Confirm", confirmation.Value);
        await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(links.ConfirmToken!, default));
        Assert.Equal(AssignmentStatus.Confirmed, (await context.Assignments.SingleAsync(x => x.Id == approved.AssignmentId)).Status);

        var reassigned = await service.AssignDirectlyAsync(opening.SlotId, "Blair Jones", "blair@example.org", null, Coordinator, default);
        Assert.Equal(AssignmentStatus.Reassigned, (await context.Assignments.SingleAsync(x => x.Id == approved.AssignmentId)).Status);
        Assert.Single(await context.Assignments.Where(x => x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed).ToListAsync());

        var declineLinks = await service.GenerateActionLinksAsync(reassigned.AssignmentId, Coordinator, default);
        await service.ApplyActionAsync(declineLinks.DeclineToken!, default);
        Assert.Single(await service.ListOpeningsAsync(default), x => x.SlotId == opening.SlotId);

        var finalAssignment = await service.AssignDirectlyAsync(opening.SlotId, "Alex Rivera", "alex@example.org", null, Coordinator, default);
        var confirmLinks = await service.GenerateActionLinksAsync(finalAssignment.AssignmentId, Coordinator, default);
        await service.ApplyActionAsync(confirmLinks.ConfirmToken!, default);
        var cancelLinks = await service.GenerateActionLinksAsync(finalAssignment.AssignmentId, Coordinator, default);
        Assert.Null(cancelLinks.ConfirmToken);
        Assert.Null(cancelLinks.DeclineToken);
        await service.ApplyActionAsync(cancelLinks.CancelToken, default);
        Assert.Single(await service.ListOpeningsAsync(default), x => x.SlotId == opening.SlotId);

        Assert.Contains(await service.GetCoverageAsync(default), item => item.SlotId == opening.SlotId && item.State == "Uncovered");
        Assert.Contains(await service.ListAuditAsync(500, default), entry => entry.Action == "AssignmentCancel");
    }

    [Fact]
    public async Task RejectingRequestLeavesPublishedSlotOpen()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await service.CreateShiftAsync("Welcome desk", null, null, starts, starts.AddHours(1), 0, Coordinator, default);
        var version = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Version;
        await service.PublishShiftAsync(shiftId, version, Coordinator, default);
        var opening = Assert.Single(await service.ListOpeningsAsync(default));
        var submission = await service.SubmitRequestAsync(opening.SlotId, "Casey", "casey@example.org", null, default);

        await service.RejectRequestAsync(submission.RequestId, Coordinator, default);

        Assert.Equal(RequestStatus.Rejected, (await context.ShiftRequests.SingleAsync()).Status);
        Assert.Single(await service.ListOpeningsAsync(default));
    }

    [Fact]
    public async Task AnonymousRequestPreservesExistingVolunteerContact()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var existingVolunteer = Volunteer.Create(
            "Alex Rivera",
            "alex@example.org",
            "555-0100",
            DateTimeOffset.UtcNow);
        context.Volunteers.Add(existingVolunteer);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await service.CreateShiftAsync("Welcome desk", null, null, starts, starts.AddHours(1), 0, Coordinator, default);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);

        await service.SubmitRequestAsync(
            shift.Slots.Single().Id,
            "Different Name",
            "ALEX@example.org",
            "555-9999",
            default);

        var persisted = await context.Volunteers.SingleAsync(x => x.Id == existingVolunteer.Id);
        Assert.Equal("Alex Rivera", persisted.Name);
        Assert.Equal("alex@example.org", persisted.Email);
        Assert.Equal("555-0100", persisted.Phone);
    }

    [Fact]
    public async Task RequestStatusKeepsItsTerminalSourceAssignment()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await service.CreateShiftAsync("Food service", null, null, starts, starts.AddHours(1), 1, Coordinator, default);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var openings = await service.ListOpeningsAsync(default);
        var primary = openings.Single(x => x.SlotLabel == "Primary");
        var backup = openings.Single(x => x.SlotLabel == "Backup 1");
        var submission = await service.SubmitRequestAsync(primary.SlotId, "Alex", "alex@example.org", null, default);
        var approved = await service.ApproveRequestAsync(submission.RequestId, Coordinator, default);

        await service.AssignDirectlyAsync(primary.SlotId, "Blair", "blair@example.org", null, Coordinator, default);
        await service.AssignDirectlyAsync(backup.SlotId, "Alex", "alex@example.org", null, Coordinator, default);

        var status = await service.GetRequestStatusAsync(submission.StatusToken, default);
        Assert.Equal("Reassigned", status.AssignmentStatus);
        Assert.Contains(
            await service.ListAuditAsync(500, default),
            entry => entry.Action == "AssignmentReassigned" && entry.EntityId == approved.AssignmentId);
    }

    [Fact]
    public async Task RequestSubmissionRejectsAStartedShift()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddHours(-1);
        var shiftId = await service.CreateShiftAsync("In progress", null, null, starts, starts.AddHours(2), 0, Coordinator, default);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.SubmitRequestAsync(shift.Slots.Single().Id, "Alex", "alex@example.org", null, default));
    }

    [Fact]
    public async Task BackupSlotEditAdvancesShiftVersion()
    {
        await _fixture.ResetAsync();
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        Guid shiftId;
        uint originalVersion;
        await using (var creationContext = _fixture.CreateContext())
        {
            var creationService = CreateService(creationContext);
            shiftId = await creationService.CreateShiftAsync(
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                Coordinator,
                default);
            originalVersion = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Version;
        }

        await using (var editContext = _fixture.CreateContext())
        {
            var editService = CreateService(editContext);
            await editService.EditShiftAsync(
                shiftId,
                originalVersion,
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                1,
                Coordinator,
                default);
        }

        await using var verificationContext = _fixture.CreateContext();
        var verificationService = CreateService(verificationContext);
        var updated = (await verificationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        Assert.NotEqual(originalVersion, updated.Version);
        Assert.Contains(updated.Slots, slot => slot.Kind == "Backup" && slot.Position == 1);
    }

    [Fact]
    public async Task BackupSlotRemovalWaitsForPendingRequestAndIsRejected()
    {
        await _fixture.ResetAsync();
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        Guid shiftId;
        Guid backupSlotId;
        uint version;
        await using (var creationContext = _fixture.CreateContext())
        {
            var creationService = CreateService(creationContext);
            shiftId = await creationService.CreateShiftAsync(
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                1,
                Coordinator,
                default);
            var shift = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            backupSlotId = shift.Slots.Single(x => x.Kind == "Backup").Id;
            version = shift.Version;
        }

        await using var requestContext = _fixture.CreateContext();
        await using var requestTransaction = await requestContext.Database.BeginTransactionAsync();
        await LockSlotAsync(requestContext, backupSlotId);

        await using var editContext = _fixture.CreateContext();
        var editService = CreateService(editContext);
        var editTask = editService.EditShiftAsync(
            shiftId,
            version,
            "Welcome desk",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            Coordinator,
            default);
        await AssertBlockedAsync(editTask);

        var volunteer = Volunteer.Create("Alex", "alex@example.org", null, DateTimeOffset.UtcNow);
        requestContext.Volunteers.Add(volunteer);
        requestContext.ShiftRequests.Add(ShiftRequest.Create(
            backupSlotId,
            volunteer.Id,
            new byte[32],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1)));
        await requestContext.SaveChangesAsync();
        await requestTransaction.CommitAsync();

        await Assert.ThrowsAsync<DomainException>(() => editTask);

        await using var verificationContext = _fixture.CreateContext();
        Assert.True((await verificationContext.ShiftSlots.SingleAsync(x => x.Id == backupSlotId)).IsActive);
    }

    [Fact]
    public async Task AssignmentWaitsForInflightRequestAndSupersedesIt()
    {
        await _fixture.ResetAsync();
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        Guid slotId;
        await using (var creationContext = _fixture.CreateContext())
        {
            var creationService = CreateService(creationContext);
            var shiftId = await creationService.CreateShiftAsync(
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                Coordinator,
                default);
            var shift = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await creationService.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            slotId = shift.Slots.Single().Id;
        }

        await using var requestContext = _fixture.CreateContext();
        await using var requestTransaction = await requestContext.Database.BeginTransactionAsync();
        await LockSlotAsync(requestContext, slotId);

        await using var assignmentContext = _fixture.CreateContext();
        var assignmentService = CreateService(assignmentContext);
        var assignmentTask = assignmentService.AssignDirectlyAsync(
            slotId,
            "Assigned Volunteer",
            "assigned@example.org",
            null,
            Coordinator,
            default);
        await AssertBlockedAsync(assignmentTask);

        var requester = Volunteer.Create("Requester", "requester@example.org", null, DateTimeOffset.UtcNow);
        var request = ShiftRequest.Create(
            slotId,
            requester.Id,
            new byte[32],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1));
        requestContext.Volunteers.Add(requester);
        requestContext.ShiftRequests.Add(request);
        await requestContext.SaveChangesAsync();
        await requestTransaction.CommitAsync();

        await assignmentTask;

        await using var verificationContext = _fixture.CreateContext();
        Assert.Equal(
            RequestStatus.Superseded,
            (await verificationContext.ShiftRequests.SingleAsync(x => x.Id == request.Id)).Status);
    }

    [Fact]
    public async Task NotificationAttemptPersistsAfterInitiatingRequestIsCanceled()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = new UnavailableNotificationService(context, new SystemClock());
        using var requestCancellation = new CancellationTokenSource();
        requestCancellation.Cancel();

        var result = await service.RecordAndSendAsync(
            new VolunteerCoordinator.Application.Notifications.NotificationMessage(
                Guid.NewGuid(),
                "AssignmentCreated",
                "alex@example.org"),
            requestCancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(NotificationState.Failed, (await context.NotificationAttempts.SingleAsync()).State);
    }

    private static Task<int> LockSlotAsync(VolunteerCoordinatorDbContext context, Guid slotId) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "ShiftSlots" WHERE "Id" = {slotId} FOR UPDATE""");

    private static async Task AssertBlockedAsync(Task operation)
    {
        var timeout = Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.Same(timeout, await Task.WhenAny(operation, timeout));
    }

    private static VolunteerCoordinatorService CreateService(VolunteerCoordinatorDbContext context)
    {
        var clock = new SystemClock();
        return new VolunteerCoordinatorService(
            new EfWorkflowStore(context),
            clock,
            new SecureTokenService(),
            new UnavailableNotificationService(context, clock));
    }
}
