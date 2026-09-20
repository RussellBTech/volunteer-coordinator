using System.Text.Json;
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
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Food service",
            "Community hall",
            "Bring badge",
            starts,
            starts.AddHours(2),
            1,
            Coordinator);

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
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Welcome desk",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            Coordinator);
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
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Welcome desk",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            Coordinator);
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
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Food service",
            null,
            null,
            starts,
            starts.AddHours(1),
            1,
            Coordinator);
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
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "In progress",
            null,
            null,
            starts,
            starts.AddHours(2),
            0,
            Coordinator);
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
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                creationService,
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                Coordinator);
            originalVersion = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Version;
        }

        await using (var editContext = _fixture.CreateContext())
        {
            var editService = CreateService(editContext);
            await ScheduleTestHelpers.EditShiftFromInstantsAsync(
                editService,
                shiftId,
                originalVersion,
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                1,
                Coordinator);
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
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                creationService,
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                1,
                Coordinator);
            var shift = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            backupSlotId = shift.Slots.Single(x => x.Kind == "Backup").Id;
            version = shift.Version;
        }

        await using var requestContext = _fixture.CreateContext();
        await using var requestTransaction = await requestContext.Database.BeginTransactionAsync();
        await LockSlotAsync(requestContext, backupSlotId);

        await using var editContext = _fixture.CreateContext();
        var editService = CreateService(editContext);
        var editTask = ScheduleTestHelpers.EditShiftFromInstantsAsync(
            editService,
            shiftId,
            version,
            "Welcome desk",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            Coordinator);
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
    public async Task EditAndDeactivateUseAscendingSlotLocksWithoutDeadlock()
    {
        await _fixture.ResetAsync();
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        Guid shiftId;
        uint version;
        await using (var creationContext = _fixture.CreateContext())
        {
            var creationService = CreateService(creationContext);
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                creationService,
                "Deterministic slot locks",
                null,
                null,
                starts,
                starts.AddHours(1),
                2,
                Coordinator);
            var shift = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await creationService.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            version = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Version;
        }

        var primarySlotId = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var firstBackupSlotId = Guid.Parse("00000000-0000-0000-0000-000000000100");
        var secondBackupSlotId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await using (var idContext = _fixture.CreateContext())
        {
            await idContext.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "ShiftSlots" SET "Id" = CASE "Position" WHEN 1 THEN {firstBackupSlotId} WHEN 2 THEN {secondBackupSlotId} END WHERE "ShiftId" = {shiftId} AND "Kind" = 1""");
            await idContext.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "ShiftSlots" SET "Id" = {primarySlotId} WHERE "ShiftId" = {shiftId} AND "Kind" = 0""");
        }

        await using var deactivationLockContext = _fixture.CreateContext();
        await using var deactivationLockTransaction = await deactivationLockContext.Database.BeginTransactionAsync();
        await LockSlotAsync(deactivationLockContext, secondBackupSlotId);

        await using var editLockContext = _fixture.CreateContext();
        await using var editLockTransaction = await editLockContext.Database.BeginTransactionAsync();
        await LockSlotAsync(editLockContext, firstBackupSlotId);

        await using var deactivationContext = _fixture.CreateContext();
        var deactivationTask = CreateService(deactivationContext).DeactivateShiftAsync(
            shiftId,
            version,
            Coordinator,
            default);
        await AssertBlockedAsync(deactivationTask);

        await using var editContext = _fixture.CreateContext();
        var editService = CreateService(editContext);
        var editTask = ScheduleTestHelpers.EditShiftFromInstantsAsync(
            editService,
            shiftId,
            version,
            "Deterministic slot locks",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            Coordinator);
        await AssertBlockedAsync(editTask);

        await deactivationLockTransaction.CommitAsync();
        await AssertBlockedAsync(deactivationTask);

        await editLockTransaction.CommitAsync();
        await deactivationTask;
        await Assert.ThrowsAsync<DomainException>(() => editTask);

        await using var verificationContext = _fixture.CreateContext();
        Assert.False((await verificationContext.Shifts.SingleAsync(x => x.Id == shiftId)).IsActive);
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
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                creationService,
                "Welcome desk",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                Coordinator);
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
    public async Task ShiftDeactivationResolvesRequestsAssignmentsTokensAndAuditsAtomically()
    {
        await _fixture.ResetAsync();
        Guid shiftId;
        Guid requestId;
        Guid assignedAssignmentId;
        Guid confirmedAssignmentId;
        string assignedConfirmToken;
        string confirmedCancelToken;
        await using (var context = _fixture.CreateContext())
        {
            var service = CreateService(context);
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Deactivation coverage",
                null,
                null,
                starts,
                starts.AddHours(2),
                2,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            var slots = shift.Slots.OrderBy(x => x.Kind).ThenBy(x => x.Position).ToArray();

            requestId = (await service.SubmitRequestAsync(
                slots[0].Id,
                "Pending Volunteer",
                "pending@example.org",
                null,
                default)).RequestId;
            assignedAssignmentId = (await service.AssignDirectlyAsync(
                slots[1].Id,
                "Assigned Volunteer",
                "assigned@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            var assignedLinks = await service.GenerateActionLinksAsync(assignedAssignmentId, Coordinator, default);
            assignedConfirmToken = assignedLinks.ConfirmToken!;

            confirmedAssignmentId = (await service.AssignDirectlyAsync(
                slots[2].Id,
                "Confirmed Volunteer",
                "confirmed@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            var confirmationLinks = await service.GenerateActionLinksAsync(confirmedAssignmentId, Coordinator, default);
            await service.ApplyActionAsync(confirmationLinks.ConfirmToken!, default);
            confirmedCancelToken = (await service.GenerateActionLinksAsync(
                confirmedAssignmentId,
                Coordinator,
                default)).CancelToken;

            var currentVersion = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Version;
            await service.DeactivateShiftAsync(shiftId, currentVersion, Coordinator, default);
        }

        await using (var verificationContext = _fixture.CreateContext())
        {
            Assert.False((await verificationContext.Shifts.SingleAsync(x => x.Id == shiftId)).IsActive);
            Assert.Equal(
                RequestStatus.Superseded,
                (await verificationContext.ShiftRequests.SingleAsync(x => x.Id == requestId)).Status);
            Assert.All(
                await verificationContext.Assignments
                    .Where(x => x.Id == assignedAssignmentId || x.Id == confirmedAssignmentId)
                    .ToListAsync(),
                assignment => Assert.Equal(AssignmentStatus.Cancelled, assignment.Status));
            Assert.All(
                await verificationContext.ActionTokens
                    .Where(x => x.AssignmentId == assignedAssignmentId || x.AssignmentId == confirmedAssignmentId)
                    .ToListAsync(),
                actionToken => Assert.NotNull(actionToken.UsedAtUtc));

            var audits = await verificationContext.AuditEntries.ToListAsync();
            Assert.Equal(
                2,
                audits.Count(x =>
                    x.Action == "AssignmentCancelledByShiftDeactivation" &&
                    x.Actor == Coordinator.ToUpperInvariant()));
            var shiftAudit = Assert.Single(audits, x => x.Action == "ShiftDeactivated" && x.EntityId == shiftId);
            using var detail = JsonDocument.Parse(shiftAudit.DetailJson);
            Assert.Equal(1, detail.RootElement.GetProperty("SupersededRequests").GetInt32());
            Assert.Equal(2, detail.RootElement.GetProperty("CancelledAssignments").GetInt32());
            Assert.Equal(5, detail.RootElement.GetProperty("InvalidatedTokens").GetInt32());
        }

        await using (var actionContext = _fixture.CreateContext())
        {
            var service = CreateService(actionContext);
            await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(assignedConfirmToken, default));
            await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(confirmedCancelToken, default));
            Assert.DoesNotContain(await service.GetCoverageAsync(default), x => x.ShiftId == shiftId);
            Assert.DoesNotContain(await service.ListOpeningsAsync(default), x => x.ShiftId == shiftId);
        }
    }

    [Fact]
    public async Task CoordinatorCancellationReopensSlotAndInvalidatesEveryIssuedLink()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Coordinator cancellation",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var assignmentId = (await service.AssignDirectlyAsync(
            shift.Slots.Single().Id,
            "Confirmed Volunteer",
            "confirmed@example.org",
            null,
            Coordinator,
            default)).AssignmentId;
        var links = await service.GenerateActionLinksAsync(assignmentId, Coordinator, default);
        await service.ApplyActionAsync(links.ConfirmToken!, default);

        await service.CancelAssignmentAsync(assignmentId, Coordinator, default);

        Assert.Equal(
            AssignmentStatus.Cancelled,
            (await context.Assignments.SingleAsync(x => x.Id == assignmentId)).Status);
        Assert.All(
            await context.ActionTokens.Where(x => x.AssignmentId == assignmentId).ToListAsync(),
            actionToken => Assert.NotNull(actionToken.UsedAtUtc));
        Assert.Contains(
            await service.GetCoverageAsync(default),
            x => x.SlotId == shift.Slots.Single().Id && x.State == "Uncovered" && x.AssignmentId is null);
        var audit = Assert.Single(
            await context.AuditEntries.ToListAsync(),
            x => x.Action == "AssignmentCancelledByCoordinator" && x.EntityId == assignmentId);
        Assert.Equal(Coordinator.ToUpperInvariant(), audit.Actor);
        Assert.Contains("\"Status\":3", audit.DetailJson);

        await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(links.DeclineToken!, default));
        await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(links.CancelToken, default));
        Assert.Equal(
            AssignmentStatus.Cancelled,
            (await context.Assignments.SingleAsync(x => x.Id == assignmentId)).Status);
    }

    [Fact]
    public async Task CoordinatorReassignmentInvalidatesSupersededAssignmentLinks()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Reassignment",
            null,
            null,
            starts,
            starts.AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = shift.Slots.Single().Id;
        var originalId = (await service.AssignDirectlyAsync(
            slotId,
            "Original Volunteer",
            "original@example.org",
            null,
            Coordinator,
            default)).AssignmentId;
        var originalLinks = await service.GenerateActionLinksAsync(originalId, Coordinator, default);

        var replacementId = (await service.AssignDirectlyAsync(
            slotId,
            "Replacement Volunteer",
            "replacement@example.org",
            null,
            Coordinator,
            default)).AssignmentId;

        Assert.Equal(
            AssignmentStatus.Reassigned,
            (await context.Assignments.SingleAsync(x => x.Id == originalId)).Status);
        Assert.All(
            await context.ActionTokens.Where(x => x.AssignmentId == originalId).ToListAsync(),
            actionToken => Assert.NotNull(actionToken.UsedAtUtc));
        await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(originalLinks.ConfirmToken!, default));
        await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(originalLinks.DeclineToken!, default));
        await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(originalLinks.CancelToken, default));
        Assert.Equal(
            AssignmentStatus.Assigned,
            (await context.Assignments.SingleAsync(x => x.Id == replacementId)).Status);
        Assert.Single(
            await context.Assignments
                .Where(x => x.ShiftSlotId == slotId &&
                            (x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed))
                .ToListAsync());
    }

    [Fact]
    public async Task ReassignmentWaitsForSupersededSlotLockBeforeGeneratingLinks()
    {
        await _fixture.ResetAsync();
        Guid sourceSlotId;
        Guid destinationSlotId;
        Guid originalAssignmentId;
        await using (var creationContext = _fixture.CreateContext())
        {
            var service = CreateService(creationContext);
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Cross-slot serialization",
                null,
                null,
                starts,
                starts.AddHours(1),
                1,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            var slots = shift.Slots.OrderBy(x => x.Kind).ThenBy(x => x.Position).ToArray();
            sourceSlotId = slots[0].Id;
            destinationSlotId = slots[1].Id;
            originalAssignmentId = (await service.AssignDirectlyAsync(
                sourceSlotId,
                "Volunteer",
                "volunteer@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            await service.GenerateActionLinksAsync(originalAssignmentId, Coordinator, default);
        }

        await using var lockContext = _fixture.CreateContext();
        await using var lockTransaction = await lockContext.Database.BeginTransactionAsync();
        await LockSlotAsync(lockContext, sourceSlotId);

        await using var linkContext = _fixture.CreateContext();
        var linkTask = CreateService(linkContext).GenerateActionLinksAsync(
            originalAssignmentId,
            Coordinator,
            default);
        await AssertBlockedAsync(linkTask);

        await using var reassignmentContext = _fixture.CreateContext();
        var reassignmentTask = CreateService(reassignmentContext).AssignDirectlyAsync(
            destinationSlotId,
            "Volunteer",
            "volunteer@example.org",
            null,
            Coordinator,
            default);
        await AssertBlockedAsync(reassignmentTask);

        await lockTransaction.CommitAsync();
        await reassignmentTask;

        var linkException = await Record.ExceptionAsync(() => linkTask);
        Assert.True(
            linkException is null or DomainException,
            $"Link generation failed with an unexpected exception: {linkException}");

        await using var verificationContext = _fixture.CreateContext();
        var originalAssignment = await verificationContext.Assignments.SingleAsync(x => x.Id == originalAssignmentId);
        Assert.Equal(AssignmentStatus.Reassigned, originalAssignment.Status);
        Assert.All(
            await verificationContext.ActionTokens
                .Where(x => x.AssignmentId == originalAssignmentId)
                .ToListAsync(),
            actionToken => Assert.NotNull(actionToken.UsedAtUtc));
        Assert.Single(
            await verificationContext.Assignments
                .Where(x => x.ShiftSlotId == destinationSlotId &&
                            (x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed))
                .ToListAsync());
    }

    [Fact]
    public async Task ReassignmentRestartsAfterStaleVolunteerSlotPreflight()
    {
        await _fixture.ResetAsync();
        Guid destinationSlotId;
        Guid sourceSlotId;
        Guid thirdSlotId;
        Guid sourceAssignmentId;
        string sourceConfirmToken;
        await using (var creationContext = _fixture.CreateContext())
        {
            var service = CreateService(creationContext);
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Three-slot reassignment",
                null,
                null,
                starts,
                starts.AddHours(1),
                2,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            var slots = shift.Slots.OrderBy(x => x.Id).ToArray();
            destinationSlotId = slots[0].Id;
            sourceSlotId = slots[1].Id;
            thirdSlotId = slots[2].Id;
            sourceAssignmentId = (await service.AssignDirectlyAsync(
                sourceSlotId,
                "Volunteer",
                "volunteer@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            sourceConfirmToken = (await service.GenerateActionLinksAsync(
                sourceAssignmentId,
                Coordinator,
                default)).ConfirmToken!;
        }

        await using var destinationLockContext = _fixture.CreateContext();
        await using var destinationLockTransaction = await destinationLockContext.Database.BeginTransactionAsync();
        await LockSlotAsync(destinationLockContext, destinationSlotId);

        await using var reassignmentContext = _fixture.CreateContext();
        var reassignmentTask = CreateService(reassignmentContext).AssignDirectlyAsync(
            destinationSlotId,
            "Volunteer",
            "volunteer@example.org",
            null,
            Coordinator,
            default);
        await AssertBlockedAsync(reassignmentTask);

        Guid thirdAssignmentId;
        string thirdConfirmToken;
        await using (var thirdSlotContext = _fixture.CreateContext())
        {
            var thirdSlotService = CreateService(thirdSlotContext);
            thirdAssignmentId = (await thirdSlotService.AssignDirectlyAsync(
                thirdSlotId,
                "Volunteer",
                "volunteer@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            thirdConfirmToken = (await thirdSlotService.GenerateActionLinksAsync(
                thirdAssignmentId,
                Coordinator,
                default)).ConfirmToken!;
        }

        await destinationLockTransaction.CommitAsync();
        await reassignmentTask;

        await using var verificationContext = _fixture.CreateContext();
        var sourceAssignment = await verificationContext.Assignments.SingleAsync(x => x.Id == sourceAssignmentId);
        var thirdAssignment = await verificationContext.Assignments.SingleAsync(x => x.Id == thirdAssignmentId);
        Assert.Equal(AssignmentStatus.Reassigned, sourceAssignment.Status);
        Assert.Equal(AssignmentStatus.Reassigned, thirdAssignment.Status);

        var activeAssignments = await verificationContext.Assignments
            .Where(x => x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed)
            .ToListAsync();
        var replacementAssignment = Assert.Single(activeAssignments);
        Assert.Equal(destinationSlotId, replacementAssignment.ShiftSlotId);

        var replacedTokens = await verificationContext.ActionTokens
            .Where(x => x.AssignmentId == sourceAssignmentId || x.AssignmentId == thirdAssignmentId)
            .ToListAsync();
        Assert.Equal(6, replacedTokens.Count);
        Assert.All(replacedTokens, actionToken => Assert.NotNull(actionToken.UsedAtUtc));
        Assert.DoesNotContain(replacedTokens, actionToken => actionToken.UsedAtUtc is null);

        await using var tokenContext = _fixture.CreateContext();
        var tokenService = CreateService(tokenContext);
        await Assert.ThrowsAsync<DomainException>(() => tokenService.ApplyActionAsync(sourceConfirmToken, default));
        await Assert.ThrowsAsync<DomainException>(() => tokenService.ApplyActionAsync(thirdConfirmToken, default));
    }

    [Fact]
    public async Task RequestsAndCoverageProjectTheSameActiveAssignmentStates()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Projection",
            null,
            null,
            starts,
            starts.AddHours(1),
            1,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var primary = shift.Slots.Single(x => x.Kind == "Primary");
        var backup = shift.Slots.Single(x => x.Kind == "Backup");
        var assignedRequest = await service.SubmitRequestAsync(
            primary.Id,
            "Assigned Requester",
            "assigned-requester@example.org",
            null,
            default);
        var confirmedRequest = await service.SubmitRequestAsync(
            backup.Id,
            "Confirmed Requester",
            "confirmed-requester@example.org",
            null,
            default);
        var assignedRequestEntity = await context.ShiftRequests.SingleAsync(x => x.Id == assignedRequest.RequestId);
        var confirmedRequestEntity = await context.ShiftRequests.SingleAsync(x => x.Id == confirmedRequest.RequestId);
        var now = DateTimeOffset.UtcNow;
        var assignedAssignment = Assignment.Create(
            primary.Id,
            shiftId,
            assignedRequestEntity.VolunteerId,
            null,
            Coordinator,
            now);
        var confirmedAssignment = Assignment.Create(
            backup.Id,
            shiftId,
            confirmedRequestEntity.VolunteerId,
            null,
            Coordinator,
            now);
        confirmedAssignment.Confirm(now);
        context.Assignments.AddRange(assignedAssignment, confirmedAssignment);
        await context.SaveChangesAsync();

        var requests = await service.ListRequestsAsync(default);
        var coverage = await service.GetCoverageAsync(default);
        var assignedProjection = requests.Single(x => x.RequestId == assignedRequest.RequestId);
        var confirmedProjection = requests.Single(x => x.RequestId == confirmedRequest.RequestId);

        Assert.Equal("Pending", assignedProjection.Status);
        Assert.Equal("Pending", confirmedProjection.Status);
        Assert.Equal("Unconfirmed", assignedProjection.SlotState);
        Assert.False(assignedProjection.CanApprove);
        Assert.Equal("Unconfirmed", coverage.Single(x => x.SlotId == primary.Id).State);
        Assert.Equal("Confirmed", confirmedProjection.SlotState);
        Assert.False(confirmedProjection.CanApprove);
        Assert.Equal("Confirmed", coverage.Single(x => x.SlotId == backup.Id).State);
    }

    [Fact]
    public async Task DeactivationWaitsForInflightSlotWorkAndResolvesPostLockState()
    {
        await _fixture.ResetAsync();
        var starts = DateTimeOffset.UtcNow.AddDays(2);
        Guid shiftId;
        Guid slotId;
        uint version;
        await using (var creationContext = _fixture.CreateContext())
        {
            var creationService = CreateService(creationContext);
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                creationService,
                "Serialized deactivation",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                Coordinator);
            var shift = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await creationService.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            var published = (await creationService.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            slotId = published.Slots.Single().Id;
            version = published.Version;
        }

        await using var inflightContext = _fixture.CreateContext();
        await using var inflightTransaction = await inflightContext.Database.BeginTransactionAsync();
        await LockSlotAsync(inflightContext, slotId);

        await using var deactivationContext = _fixture.CreateContext();
        var deactivationTask = CreateService(deactivationContext).DeactivateShiftAsync(
            shiftId,
            version,
            Coordinator,
            default);
        await AssertBlockedAsync(deactivationTask);

        var now = DateTimeOffset.UtcNow;
        var requester = Volunteer.Create("Requester", "requester@example.org", null, now);
        var assignedVolunteer = Volunteer.Create("Assigned", "assigned@example.org", null, now);
        var request = ShiftRequest.Create(
            slotId,
            requester.Id,
            new SecureTokenService().Generate().Hash,
            now,
            now.AddDays(1));
        var assignment = Assignment.Create(
            slotId,
            shiftId,
            assignedVolunteer.Id,
            null,
            Coordinator,
            now);
        assignment.Confirm(now);
        var generatedToken = new SecureTokenService().Generate();
        var actionToken = ActionToken.Create(
            assignment.Id,
            VolunteerAction.Cancel,
            generatedToken.Hash,
            now,
            now.AddDays(1));
        inflightContext.Volunteers.AddRange(requester, assignedVolunteer);
        inflightContext.ShiftRequests.Add(request);
        inflightContext.Assignments.Add(assignment);
        inflightContext.ActionTokens.Add(actionToken);
        await inflightContext.SaveChangesAsync();
        await inflightTransaction.CommitAsync();

        await deactivationTask;

        await using var verificationContext = _fixture.CreateContext();
        Assert.False((await verificationContext.Shifts.SingleAsync(x => x.Id == shiftId)).IsActive);
        Assert.Equal(
            RequestStatus.Superseded,
            (await verificationContext.ShiftRequests.SingleAsync(x => x.Id == request.Id)).Status);
        Assert.Equal(
            AssignmentStatus.Cancelled,
            (await verificationContext.Assignments.SingleAsync(x => x.Id == assignment.Id)).Status);
        Assert.NotNull(
            (await verificationContext.ActionTokens.SingleAsync(x => x.Id == actionToken.Id)).UsedAtUtc);
    }

    [Fact]
    public async Task StaleAssignmentAndTokenTransitionRollsBackAfterCoordinatorCancellationWins()
    {
        await _fixture.ResetAsync();
        Guid assignmentId;
        string confirmToken;
        await using (var creationContext = _fixture.CreateContext())
        {
            var service = CreateService(creationContext);
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Stale token transition",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            assignmentId = (await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Volunteer",
                "volunteer@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            confirmToken = (await service.GenerateActionLinksAsync(
                assignmentId,
                Coordinator,
                default)).ConfirmToken!;
        }

        await using var staleContext = _fixture.CreateContext();
        var staleAssignment = await staleContext.Assignments.SingleAsync(x => x.Id == assignmentId);
        var confirmHash = new SecureTokenService().Hash(confirmToken);
        var staleActionToken = await staleContext.ActionTokens
            .SingleAsync(x => x.TokenHash.SequenceEqual(confirmHash));

        DateTimeOffset? cancellationTimestamp;
        await using (var winningContext = _fixture.CreateContext())
        {
            await CreateService(winningContext).CancelAssignmentAsync(assignmentId, Coordinator, default);
            var cancelledToken = await winningContext.ActionTokens
                .SingleAsync(x => x.TokenHash.SequenceEqual(confirmHash));
            await winningContext.Entry(cancelledToken).ReloadAsync();
            cancellationTimestamp = cancelledToken.UsedAtUtc;
        }

        var staleStore = new EfWorkflowStore(staleContext);
        await Assert.ThrowsAsync<DomainException>(() =>
            staleStore.ExecuteInTransactionAsync(
                _ =>
                {
                    var now = DateTimeOffset.UtcNow;
                    staleActionToken.Consume(now);
                    staleAssignment.Confirm(now);
                    return Task.FromResult(true);
                },
                default));

        await using var verificationContext = _fixture.CreateContext();
        Assert.Equal(
            AssignmentStatus.Cancelled,
            (await verificationContext.Assignments.SingleAsync(x => x.Id == assignmentId)).Status);
        Assert.Equal(
            cancellationTimestamp,
            (await verificationContext.ActionTokens
                .SingleAsync(x => x.TokenHash.SequenceEqual(confirmHash))).UsedAtUtc);
    }

    [Fact]
    public async Task StaleTrackedAssignmentCannotGenerateOrApplyLinksAfterDeactivation()
    {
        await _fixture.ResetAsync();
        Guid shiftId;
        Guid assignmentId;
        uint version;
        string confirmToken;
        await using (var creationContext = _fixture.CreateContext())
        {
            var service = CreateService(creationContext);
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Stale tracked assignment",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            assignmentId = (await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Volunteer",
                "volunteer@example.org",
                null,
                Coordinator,
                default)).AssignmentId;
            confirmToken = (await service.GenerateActionLinksAsync(
                assignmentId,
                Coordinator,
                default)).ConfirmToken!;
            version = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Version;
        }

        await using var staleContext = _fixture.CreateContext();
        await staleContext.Assignments.SingleAsync(x => x.Id == assignmentId);
        var confirmHash = new SecureTokenService().Hash(confirmToken);
        await staleContext.ActionTokens.SingleAsync(x => x.TokenHash.SequenceEqual(confirmHash));

        await using (var deactivationContext = _fixture.CreateContext())
        {
            await CreateService(deactivationContext).DeactivateShiftAsync(
                shiftId,
                version,
                Coordinator,
                default);
        }

        var staleService = CreateService(staleContext);
        await Assert.ThrowsAsync<DomainException>(() =>
            staleService.GenerateActionLinksAsync(assignmentId, Coordinator, default));
        await Assert.ThrowsAsync<DomainException>(() =>
            staleService.ApplyActionAsync(confirmToken, default));

        await using var verificationContext = _fixture.CreateContext();
        var persistedTokens = await verificationContext.ActionTokens
            .Where(x => x.AssignmentId == assignmentId)
            .ToListAsync();
        Assert.Equal(3, persistedTokens.Count);
        Assert.All(persistedTokens, actionToken => Assert.NotNull(actionToken.UsedAtUtc));
        Assert.Single(
            await verificationContext.AuditEntries
                .Where(x => x.Action == "ActionLinksGenerated" && x.EntityId == assignmentId)
                .ToListAsync());
    }

    [Fact]
    public async Task NotificationAttemptPersistsAfterInitiatingRequestIsCanceled()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var volunteer = Volunteer.Create(
            "Alex",
            "alex@example.org",
            null,
            DateTimeOffset.UtcNow);
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();

        var service = new UnavailableNotificationService(context, new SystemClock());
        using var requestCancellation = new CancellationTokenSource();
        requestCancellation.Cancel();

        var result = await service.RecordAndSendAsync(
            new VolunteerCoordinator.Application.Notifications.NotificationMessage(
                Guid.NewGuid(),
                "AssignmentCreated",
                volunteer.Id),
            requestCancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(NotificationState.Failed, (await context.NotificationAttempts.SingleAsync()).State);
        Assert.Equal("alex@example.org", (await context.NotificationAttempts.SingleAsync()).Destination);
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
