using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
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
        var confirmation = await service.ApplyActionAsync(links.ConfirmToken, default);
        Assert.Equal("Confirm", confirmation.Value);
        await Assert.ThrowsAsync<DomainException>(() => service.ApplyActionAsync(links.ConfirmToken, default));
        Assert.Equal(AssignmentStatus.Confirmed, (await context.Assignments.SingleAsync(x => x.Id == approved.AssignmentId)).Status);

        var reassigned = await service.AssignDirectlyAsync(opening.SlotId, "Blair Jones", "blair@example.org", null, Coordinator, default);
        Assert.Equal(AssignmentStatus.Reassigned, (await context.Assignments.SingleAsync(x => x.Id == approved.AssignmentId)).Status);
        Assert.Single(await context.Assignments.Where(x => x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed).ToListAsync());

        var declineLinks = await service.GenerateActionLinksAsync(reassigned.AssignmentId, Coordinator, default);
        await service.ApplyActionAsync(declineLinks.DeclineToken, default);
        Assert.Single(await service.ListOpeningsAsync(default), x => x.SlotId == opening.SlotId);

        var finalAssignment = await service.AssignDirectlyAsync(opening.SlotId, "Alex Rivera", "alex@example.org", null, Coordinator, default);
        var confirmLinks = await service.GenerateActionLinksAsync(finalAssignment.AssignmentId, Coordinator, default);
        await service.ApplyActionAsync(confirmLinks.ConfirmToken, default);
        var cancelLinks = await service.GenerateActionLinksAsync(finalAssignment.AssignmentId, Coordinator, default);
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
