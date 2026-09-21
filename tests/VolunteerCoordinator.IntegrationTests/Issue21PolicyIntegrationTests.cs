using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Commitments;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue21PolicyIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue21PolicyIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Direct_claim_race_commits_one_assignment_and_no_request()
    {
        await _fixture.ResetAsync();
        await using var setupContext = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        var setup = ScheduleTestHelpers.CreateService(setupContext, clock);
        var settings = await ScheduleTestHelpers.EnsureSettingsAsync(setup, Coordinator);
        var schedule = ScheduleTestHelpers.ForInstantRange(
            Now.AddDays(2),
            Now.AddDays(2).AddHours(2),
            settings);
        var shiftId = await setup.CreateShiftAsync(
            "Direct claim race",
            null,
            null,
            "Serve together.",
            schedule,
            0,
            Coordinator,
            default,
            SignupPolicy.DirectClaim);
        var shift = (await setup.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
        await setup.PublishShiftAsync(shiftId, shift.Version, Coordinator, default);
        var slotId = (await setup.ListOpeningsAsync(default)).Single().SlotId;

        async Task<string> ClaimAsync(string name, string email)
        {
            await using var context = _fixture.CreateContext();
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var result = await service.SubmitRequestAsync(slotId, name, email, null, default);
            return result.StatusToken;
        }

        var first = ClaimAsync("First claimant", "first-claimant@example.org");
        var second = ClaimAsync("Second claimant", "second-claimant@example.org");
        var outcomes = await Task.WhenAll(
            first.ContinueWith(task => (Success: task.Status == TaskStatus.RanToCompletion, Error: task.Exception?.GetBaseException())),
            second.ContinueWith(task => (Success: task.Status == TaskStatus.RanToCompletion, Error: task.Exception?.GetBaseException())));

        Assert.Single(outcomes, x => x.Success);
        var loser = Assert.Single(outcomes, x => !x.Success);
        Assert.IsType<DomainException>(loser.Error);
        Assert.Equal(
            "This commitment was just claimed. Choose another opening.",
            loser.Error!.Message);

        await using var verification = _fixture.CreateContext();
        Assert.Single(await verification.Assignments.Where(x => x.Status == AssignmentStatus.Confirmed).ToListAsync());
        Assert.Empty(await verification.ShiftRequests.ToListAsync());
        Assert.Equal(1, await verification.Volunteers.CountAsync());
        Assert.Single(await verification.VolunteerAccessCapabilities.ToListAsync());
    }

    [Fact]
    public async Task Recurring_direct_claim_materializes_one_confirmed_occurrence_and_hub()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await ScheduleTestHelpers.EnsureSettingsAsync(workflow, Coordinator);
        var store = new EfWorkflowStore(context);
        var schedules = new RecurringShiftService(store, store, clock);
        var firstDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);
        var input = new VolunteerCoordinator.Application.Models.RecurringSeriesInput(
            "Recurring direct claim",
            null,
            "Bring comfortable shoes.",
            null,
            RecurrenceKind.Daily,
            1,
            [],
            firstDate,
            new TimeOnly(9, 0),
            60,
            0,
            4,
            AmbiguousTimeChoice.FirstOccurrence,
            settings.Version,
            SignupPolicy.DirectClaim);
        var seriesId = await schedules.CreateRecurringSeriesAsync(input, Coordinator, default);
        var occurrence = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId && x.Status == RecurringOccurrenceStatus.Generated)
            .OrderBy(x => x.LocalDate)
            .FirstAsync();
        var shift = await context.Shifts.SingleAsync(x => x.Id == occurrence.ShiftId);
        shift.Publish(clock.UtcNow);
        await context.SaveChangesAsync();

        var commitments = new RecurringCommitmentService(
            store,
            store,
            store,
            clock,
            new VolunteerCoordinator.Infrastructure.Security.SecureTokenService());
        var series = await store.GetRecurringSeriesAsync(seriesId, default)
            ?? throw new InvalidOperationException("Series was not persisted.");
        var result = await commitments.SubmitAsync(
            seriesId,
            SlotKind.Primary,
            1,
            occurrence.LocalDate,
            4,
            series.Version,
            "Recurring claimant",
            "recurring-claimant@example.org",
            null,
            default);

        var hub = await commitments.InspectHubAsync(result.StatusToken, default);
        Assert.Equal(RecurringCommitmentState.Active.ToString(), hub.Status);
        Assert.Contains(hub.Dates, x => x.State == RecurringCommitmentOccurrenceState.Confirmed.ToString());
        Assert.Single(await context.Assignments.Where(x => x.Status == AssignmentStatus.Confirmed).ToListAsync());
    }

    [Fact]
    public async Task Approval_required_recurring_request_confirms_once()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await ScheduleTestHelpers.EnsureSettingsAsync(workflow, Coordinator);
        var store = new EfWorkflowStore(context);
        var schedules = new RecurringShiftService(store, store, clock);
        var firstDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);
        var input = new VolunteerCoordinator.Application.Models.RecurringSeriesInput(
            "Recurring approval",
            null,
            null,
            null,
            RecurrenceKind.Daily,
            1,
            [],
            firstDate,
            new TimeOnly(10, 0),
            60,
            0,
            4,
            AmbiguousTimeChoice.FirstOccurrence,
            settings.Version,
            SignupPolicy.ApprovalRequired);
        var seriesId = await schedules.CreateRecurringSeriesAsync(input, Coordinator, default);
        var occurrence = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId && x.Status == RecurringOccurrenceStatus.Generated)
            .OrderBy(x => x.LocalDate)
            .FirstAsync();
        var shift = await context.Shifts.SingleAsync(x => x.Id == occurrence.ShiftId);
        shift.Publish(clock.UtcNow);
        await context.SaveChangesAsync();

        var commitments = new RecurringCommitmentService(
            store,
            store,
            store,
            clock,
            new VolunteerCoordinator.Infrastructure.Security.SecureTokenService());
        var series = await store.GetRecurringSeriesAsync(seriesId, default)
            ?? throw new InvalidOperationException("Series was not persisted.");
        var request = await commitments.SubmitAsync(
            seriesId,
            SlotKind.Primary,
            1,
            occurrence.LocalDate,
            4,
            series.Version,
            "Approval claimant",
            "approval-claimant@example.org",
            null,
            default);
        await commitments.ApproveAsync(request.RequestId, Coordinator, default);

        var awaiting = await commitments.InspectHubAsync(request.StatusToken, default);
        Assert.Equal(RecurringCommitmentState.AwaitingConfirmation.ToString(), awaiting.Status);
        Assert.True(awaiting.CanConfirm);
        await commitments.ApplyHubActionAsync(request.StatusToken, "Confirm", null, default);
        var active = await commitments.InspectHubAsync(request.StatusToken, default);
        Assert.Equal(RecurringCommitmentState.Active.ToString(), active.Status);
        Assert.False(active.CanConfirm);
        await commitments.ApplyHubActionAsync(request.StatusToken, "Confirm", null, default);
        Assert.Single(await context.Assignments.Where(x => x.Status == AssignmentStatus.Confirmed).ToListAsync());
    }
    [Fact]
    public async Task Recurring_preview_rejects_when_no_occurrence_is_eligible()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await ScheduleTestHelpers.EnsureSettingsAsync(workflow, Coordinator);
        var store = new EfWorkflowStore(context);
        var schedules = new RecurringShiftService(store, store, clock);
        var firstDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);
        var seriesId = await schedules.CreateRecurringSeriesAsync(
            new VolunteerCoordinator.Application.Models.RecurringSeriesInput(
                "No eligible preview",
                null,
                null,
                null,
                RecurrenceKind.Daily,
                1,
                [],
                firstDate,
                new TimeOnly(9, 0),
                60,
                0,
                4,
                AmbiguousTimeChoice.FirstOccurrence,
                settings.Version,
                SignupPolicy.ApprovalRequired),
            Coordinator,
            default);
        var commitments = new RecurringCommitmentService(
            store,
            store,
            store,
            clock,
            new VolunteerCoordinator.Infrastructure.Security.SecureTokenService());

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            commitments.PreviewAsync(
                seriesId,
                SlotKind.Primary,
                1,
                firstDate,
                4,
                default));

        Assert.Contains("No eligible", exception.Message, StringComparison.Ordinal);
    }
}
