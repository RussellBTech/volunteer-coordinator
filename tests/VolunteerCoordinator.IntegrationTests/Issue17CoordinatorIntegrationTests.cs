using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue17CoordinatorIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue17CoordinatorIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CoordinatorHomeGuidesSetupThenProjectsBoundedRoutineAttention()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var service = CreateService(context, clock);

        var emptyHome = await service.GetCoordinatorHomeAsync(default);
        Assert.True(emptyHome.IsSetupMode);
        Assert.Equal(5, emptyHome.SetupSteps.Count);
        Assert.Equal("Set your group time zone", emptyHome.RecommendedActionLabel);
        Assert.Equal("/Coordinator/Settings", emptyHome.RecommendedActionUrl);

        var settings = await service.ConfigureGroupTimeZoneAsync(
            "Etc/UTC",
            null,
            false,
            Coordinator,
            default);
        var noShiftHome = await service.GetCoordinatorHomeAsync(default);
        Assert.True(noShiftHome.IsSetupMode);
        Assert.Equal("Create the first schedule entry", noShiftHome.RecommendedActionLabel);

        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Community meal",
            "Community hall",
            "Bring a badge.",
            FixedNow.AddDays(1),
            FixedNow.AddDays(1).AddHours(2),
            2,
            Coordinator,
            cancellationToken: default);
        var unpublishedHome = await service.GetCoordinatorHomeAsync(default);
        Assert.True(unpublishedHome.IsSetupMode);
        Assert.Equal("Review and publish the first schedule entry", unpublishedHome.RecommendedActionLabel);
        Assert.Contains(unpublishedHome.SetupSteps, x => x.Number == 4 && x.IsCurrent);

        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        foreach (var slot in shift.Slots)
        {
            await service.SubmitRequestAsync(
                slot.Id,
                $"Volunteer {slot.Position}",
                $"volunteer-{slot.Position}@example.org",
                null,
                default);
        }

        var routineHome = await service.GetCoordinatorHomeAsync(default);
        Assert.False(routineHome.IsSetupMode);
        Assert.Equal(3, routineHome.PendingRequestCount);
        Assert.Equal(3, routineHome.UncoveredCommitmentCount);
        Assert.Equal(3, routineHome.FailedMessageCount);
        Assert.Equal(3, routineHome.Attention.Single(x => x.Key == "pending").Examples.Count);
        Assert.Equal(3, routineHome.Attention.Single(x => x.Key == "message").Examples.Count);
        Assert.All(routineHome.Attention, card => Assert.DoesNotContain(shiftId.ToString(), card.Url, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("UTC", routineHome.Attention.Single(x => x.Key == "message").Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KnownVolunteerPreviewAndConfirmationDoNotRequireRetypedContactData()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var volunteer = Volunteer.Create("Known Volunteer", "known@example.org", "555-0100", FixedNow);
        context.Volunteers.Add(volunteer);
        await context.SaveChangesAsync();
        var service = CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Known volunteer coverage",
            null,
            null,
            FixedNow.AddDays(1),
            FixedNow.AddDays(1).AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = shift.Slots.Single().Id;

        var preview = await service.GetAssignmentPreviewAsync(
            slotId,
            volunteer.Id,
            null,
            null,
            null,
            default);
        Assert.Equal("Known Volunteer", preview.ReplacementVolunteer?.Name);
        Assert.Equal("known@example.org", preview.ReplacementVolunteer?.Email);

        await service.AssignVolunteerAsync(
            slotId,
            preview.ExpectedAssignmentId,
            preview.ExpectedVolunteerId,
            preview.ExpectedAssignmentState,
            preview.ExpectedShiftVersion,
            volunteer.Id,
            null,
            null,
            null,
            Coordinator,
            default);

        Assert.Equal(
            volunteer.Id,
            (await context.Assignments.SingleAsync(x => x.ShiftSlotId == slotId)).VolunteerId);
        Assert.Equal("known@example.org", (await context.Volunteers.SingleAsync(x => x.Id == volunteer.Id)).Email);
    }

    [Fact]
    public async Task DeactivationRejectsAStaleAffectedSetWithoutChangingTheSchedule()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var service = CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Stale review",
            null,
            null,
            FixedNow.AddDays(1),
            FixedNow.AddDays(1).AddHours(1),
            1,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        await service.SubmitRequestAsync(
            shift.Slots.First().Id,
            "First requester",
            "first-requester@example.org",
            null,
            default);

        var preview = await service.GetDeactivatePreviewAsync(shiftId, default);
        await service.SubmitRequestAsync(
            shift.Slots.Skip(1).Single().Id,
            "Second requester",
            "second-requester@example.org",
            null,
            default);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            service.DeactivateShiftAsync(
                shiftId,
                preview.ExpectedShiftVersion,
                Coordinator,
                default,
                preview.ExpectedAffectedSet));
        Assert.Equal(VolunteerCoordinatorService.StalePreviewMessage, exception.Message);
        Assert.True((await context.Shifts.SingleAsync(x => x.Id == shiftId)).IsActive);
    }
    [Fact]
    public async Task CoverageIncludesInProgressCommitmentsLikeCoordinatorHome()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var service = CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "In-progress coverage",
            null,
            null,
            FixedNow.AddHours(-1),
            FixedNow.AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);

        var home = await service.GetCoordinatorHomeAsync(default);
        var coverage = await service.GetCoverageAsync(default);

        Assert.Equal(1, home.UncoveredCommitmentCount);
        Assert.Contains(coverage, item => item.ShiftId == shiftId && item.State == "Uncovered");
    }
    [Fact]
    public async Task HomeAttentionExamplesAreUrgencyOrderedBeforeTheBoundedTake()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var service = CreateService(context, clock);
        var starts = new[]
        {
            FixedNow.AddHours(-1),
            FixedNow.AddHours(4),
            FixedNow.AddHours(2),
            FixedNow.AddHours(3)
        };
        foreach (var (start, index) in starts.Select((value, index) => (value, index)))
        {
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                $"Urgency {index}",
                null,
                null,
                start,
                index == 0 ? FixedNow.AddHours(2) : start.AddHours(1),
                0,
                Coordinator);
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        }

        var home = await service.GetCoordinatorHomeAsync(default);
        var examples = home.Attention.Single(x => x.Key == "uncovered").Examples;

        Assert.Equal(4, home.UncoveredCommitmentCount);
        Assert.Equal(3, examples.Count);
        Assert.Equal(
            starts.OrderBy(x => x).Take(3),
            examples.Select(x => x.Commitment.StartsAtUtc));
    }

    [Fact]
    public async Task ExpiredUnpublishedShiftOffersEditRecoveryWithoutPublicationPreview()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var service = CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Expired draft",
            null,
            null,
            FixedNow.AddHours(-2),
            FixedNow.AddHours(-1),
            0,
            Coordinator);

        var home = await service.GetCoordinatorHomeAsync(default);

        Assert.Equal("Edit the expired schedule entry", home.RecommendedActionLabel);
        Assert.Equal($"/Coordinator/Schedule/Edit/{shiftId}", home.RecommendedActionUrl);
        Assert.Contains(
            home.SetupSteps,
            step => step.Number == 4 &&
                    step.IsCurrent &&
                    step.Url == $"/Coordinator/Schedule/Edit/{shiftId}");
        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            service.GetPublishPreviewAsync(shiftId, default));
        Assert.Contains("ended", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateEmailPreviewMatchesExistingContactReuseAndMutation()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var existing = Volunteer.Create("Original name", "reuse@example.org", "555-0100", FixedNow);
        context.Volunteers.Add(existing);
        await context.SaveChangesAsync();
        var service = CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Duplicate email assignment",
            null,
            null,
            FixedNow.AddDays(1),
            FixedNow.AddDays(1).AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = shift.Slots.Single().Id;

        var preview = await service.GetAssignmentPreviewAsync(
            slotId,
            null,
            "Updated name",
            "reuse@example.org",
            "555-0199",
            default);

        Assert.True(preview.ReplacementReusesExistingVolunteer);
        Assert.Equal("Updated name", preview.ReplacementVolunteer?.Name);
        Assert.Equal("reuse@example.org", preview.ReplacementVolunteer?.Email);
        Assert.Equal("555-0199", preview.ReplacementVolunteer?.Phone);
        Assert.Contains(
            preview.Consequences,
            consequence => consequence.Contains("updated and reused", StringComparison.OrdinalIgnoreCase));

        await service.AssignVolunteerAsync(
            slotId,
            preview.ExpectedAssignmentId,
            preview.ExpectedVolunteerId,
            preview.ExpectedAssignmentState,
            preview.ExpectedShiftVersion,
            null,
            "Updated name",
            "reuse@example.org",
            "555-0199",
            Coordinator,
            default,
            preview.ExpectedSettingsVersion);

        var saved = await context.Volunteers.SingleAsync(x => x.Id == existing.Id);
        Assert.Equal("Updated name", saved.Name);
        Assert.Equal("555-0199", saved.Phone);
        Assert.Equal(existing.Id, (await context.Assignments.SingleAsync()).VolunteerId);
    }

    [Fact]
    public async Task AssignmentConfirmationRejectsAStaleGroupTimeZonePreview()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var service = CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Time zone stale assignment",
            null,
            null,
            FixedNow.AddDays(1),
            FixedNow.AddDays(1).AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = shift.Slots.Single().Id;
        var preview = await service.GetAssignmentPreviewAsync(
            slotId,
            null,
            "Zone stale",
            "zone-stale@example.org",
            null,
            default);
        var settings = await service.GetGroupSettingsAsync(default);
        Assert.NotNull(settings);
        await service.ConfigureGroupTimeZoneAsync(
            "America/New_York",
            settings!.Version,
            true,
            Coordinator,
            default);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            service.AssignVolunteerAsync(
                slotId,
                preview.ExpectedAssignmentId,
                preview.ExpectedVolunteerId,
                preview.ExpectedAssignmentState,
                preview.ExpectedShiftVersion,
                null,
                "Zone stale",
                "zone-stale@example.org",
                null,
                Coordinator,
                default,
                preview.ExpectedSettingsVersion));

        Assert.Equal(VolunteerCoordinatorService.StalePreviewMessage, exception.Message);
        Assert.Empty(await context.Assignments.ToListAsync());
    }

    [Fact]
    public async Task ActionableMessagesIncludeAssignmentDeliveryFailures()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        await using var context = _fixture.CreateContext();
        var service = CreateService(context, clock);
        var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
            service,
            "Assignment message failure",
            null,
            null,
            FixedNow.AddDays(1),
            FixedNow.AddDays(1).AddHours(1),
            0,
            Coordinator);
        var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await service.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        await service.AssignDirectlyAsync(
            shift.Slots.Single().Id,
            "Assigned follow-up",
            "assigned-follow-up@example.org",
            null,
            Coordinator,
            default);

        var page = await service.GetActionableMessagesPageAsync(1, default);

        Assert.Single(page.Messages);
        Assert.Equal("Assignment update", page.Messages[0].Purpose);
        Assert.Equal("assigned-follow-up@example.org", page.Messages[0].VolunteerEmail);
    }

    [Fact]
    public async Task ActionableMessagesStayBoundedAndPaginatedPastTwoHundred()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        context.GroupSettings.Add(GroupSettings.Create("Etc/UTC"));
        var tokens = new SecureTokenService();
        const int messageCount = 205;
        for (var index = 0; index < messageCount; index++)
        {
            var starts = index == 0
                ? FixedNow.AddHours(-1)
                : FixedNow.AddHours(1).AddMinutes(index);
            var shift = Shift.Create(
                $"Message commitment {index:D3}",
                null,
                null,
                starts,
                index == 0 ? FixedNow.AddHours(2) : starts.AddHours(1),
                0);
            shift.Publish(FixedNow);
            var slot = shift.Slots.Single();
            var volunteer = Volunteer.Create(
                $"Message volunteer {index:D3}",
                $"message-{index:D3}@example.org",
                index == 0 ? "555-0200" : null,
                FixedNow);
            var generated = tokens.Generate();
            var request = ShiftRequest.Create(slot.Id, volunteer.Id, FixedNow);
            var attempt = NotificationAttempt.Create(
                request.Id,
                "RequestReceived",
                volunteer.Email,
                FixedNow);
            attempt.Fail(FixedNow, "safe test failure");
            context.AddRange(shift, volunteer, request, attempt);
        }

        await context.SaveChangesAsync();
        var service = CreateService(context, new ScheduleTestHelpers.FixedClock(FixedNow));
        var firstPage = await service.GetActionableMessagesPageAsync(1, default);
        var lastPage = await service.GetActionableMessagesPageAsync(5, default);

        Assert.Equal(messageCount, firstPage.TotalCount);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(50, firstPage.Messages.Count);
        Assert.Equal(5, lastPage.Page);
        Assert.Equal(5, lastPage.Messages.Count);
        Assert.Equal("message-000@example.org", firstPage.Messages[0].VolunteerEmail);
        Assert.Equal("555-0200", firstPage.Messages[0].VolunteerPhone);
    }
    private static VolunteerCoordinatorService CreateService(
        VolunteerCoordinatorDbContext context,
        ScheduleTestHelpers.FixedClock clock) =>
        new(
            new EfWorkflowStore(context),
            clock,
            new SecureTokenService(),
            new UnavailableNotificationService(context, clock));
}
