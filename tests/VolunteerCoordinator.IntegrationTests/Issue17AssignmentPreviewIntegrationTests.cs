using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue17AssignmentPreviewIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue17AssignmentPreviewIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AssignmentPreviewListsEveryCollateralMutationAndRejectsSeparateContextDrift()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        Guid targetSlotId;
        Guid otherSlotId;
        Guid currentAssignmentId;
        Guid otherAssignmentId;
        Guid pendingRequestId;
        Guid selectedVolunteerId;
        CoordinatorActionPreviewDto preview;

        await using (var context = _fixture.CreateContext())
        {
            var currentVolunteer = Volunteer.Create(
                "Current volunteer",
                "current@example.org",
                null,
                FixedNow);
            var selectedVolunteer = Volunteer.Create(
                "Selected volunteer",
                "selected@example.org",
                null,
                FixedNow);
            var requester = Volunteer.Create(
                "Pending requester",
                "pending@example.org",
                null,
                FixedNow);
            context.Volunteers.AddRange(currentVolunteer, selectedVolunteer, requester);
            await context.SaveChangesAsync();

            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Collateral review",
                null,
                null,
                FixedNow.AddDays(1),
                FixedNow.AddDays(1).AddHours(1),
                1,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
            targetSlotId = shift.Slots.Single(x => x.Kind == "Primary").Id;
            otherSlotId = shift.Slots.Single(x => x.Kind == "Backup").Id;

            currentAssignmentId = (await service.AssignDirectlyAsync(
                targetSlotId,
                currentVolunteer.Name,
                currentVolunteer.Email,
                null,
                Coordinator,
                default)).AssignmentId;
            otherAssignmentId = (await service.AssignDirectlyAsync(
                otherSlotId,
                selectedVolunteer.Name,
                selectedVolunteer.Email,
                null,
                Coordinator,
                default)).AssignmentId;

            var token = new SecureTokenService().Generate();
            var pendingRequest = ShiftRequest.Create(targetSlotId, requester.Id, FixedNow);
            context.ShiftRequests.Add(pendingRequest);
            await context.SaveChangesAsync();
            pendingRequestId = pendingRequest.Id;
            selectedVolunteerId = selectedVolunteer.Id;

            preview = await service.GetAssignmentPreviewAsync(
                targetSlotId,
                selectedVolunteerId,
                null,
                null,
                null,
                default);
        }

        Assert.Equal(3, preview.AffectedPeople.Count);
        Assert.Contains(preview.AffectedPeople, person => person.AffectedAssignmentId == currentAssignmentId);
        Assert.Contains(preview.AffectedPeople, person => person.AffectedAssignmentId == otherAssignmentId);
        Assert.Contains(preview.AffectedPeople, person => person.AffectedRequestId == pendingRequestId);
        Assert.Contains("volunteers:", preview.ExpectedAffectedSet, StringComparison.Ordinal);
        Assert.Contains(currentAssignmentId.ToString("N"), preview.ExpectedAffectedSet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(otherAssignmentId.ToString("N"), preview.ExpectedAffectedSet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(pendingRequestId.ToString("N"), preview.ExpectedAffectedSet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            preview.Consequences,
            consequence => consequence.Contains("other assignment", StringComparison.OrdinalIgnoreCase));

        Guid driftRequestId;
        await using (var raceContext = _fixture.CreateContext())
        {
            var driftVolunteer = Volunteer.Create(
                "Drift requester",
                "drift@example.org",
                null,
                FixedNow);
            raceContext.Volunteers.Add(driftVolunteer);
            await raceContext.SaveChangesAsync();
            var token = new SecureTokenService().Generate();
            var driftRequest = ShiftRequest.Create(targetSlotId, driftVolunteer.Id, FixedNow);
            raceContext.ShiftRequests.Add(driftRequest);
            await raceContext.SaveChangesAsync();
            driftRequestId = driftRequest.Id;
        }

        await using (var confirmationContext = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(confirmationContext, clock);
            var exception = await Assert.ThrowsAsync<DomainException>(() => service.AssignVolunteerAsync(
                targetSlotId,
                preview.ExpectedAssignmentId,
                preview.ExpectedVolunteerId,
                preview.ExpectedAssignmentState,
                preview.ExpectedShiftVersion,
                selectedVolunteerId,
                null,
                null,
                null,
                Coordinator,
                default,
                preview.ExpectedSettingsVersion,
                preview.ExpectedAffectedSet,
                preview.ExpectedSelectedVolunteerId,
                preview.ExpectedSelectedVolunteerNormalizedEmail));
            Assert.Equal(VolunteerCoordinatorService.StalePreviewMessage, exception.Message);
        }

        await using (var verificationContext = _fixture.CreateContext())
        {
            Assert.Equal(
                AssignmentStatus.Assigned,
                (await verificationContext.Assignments.SingleAsync(x => x.Id == currentAssignmentId)).Status);
            Assert.Equal(
                AssignmentStatus.Assigned,
                (await verificationContext.Assignments.SingleAsync(x => x.Id == otherAssignmentId)).Status);
            Assert.Equal(
                RequestStatus.Pending,
                (await verificationContext.ShiftRequests.SingleAsync(x => x.Id == pendingRequestId)).Status);
            Assert.Equal(
                RequestStatus.Pending,
                (await verificationContext.ShiftRequests.SingleAsync(x => x.Id == driftRequestId)).Status);
        }
    }
}
