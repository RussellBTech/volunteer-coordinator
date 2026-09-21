using VolunteerCoordinator.Domain;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue20RecurringIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue20RecurringIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GenerationIsBoundedAndIdempotentWithUniqueLocalDates()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var workflow = ScheduleTestHelpers.CreateService(context, new ScheduleTestHelpers.FixedClock(FixedNow));
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), new ScheduleTestHelpers.FixedClock(FixedNow));
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Daily, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 2),
            Coordinator,
            default);

        var initialCount = await context.RecurringShiftOccurrences.CountAsync(x => x.SeriesId == seriesId);
        Assert.InRange(initialCount, 14, 16);
        await recurrence.GenerateSeriesThroughAsync(seriesId, new DateOnly(2026, 4, 1), default);
        await recurrence.GenerateSeriesThroughAsync(seriesId, new DateOnly(2026, 4, 1), default);

        await using var verification = _fixture.CreateContext();
        var occurrences = await verification.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .ToListAsync();
        Assert.Equal(occurrences.Count, occurrences.Select(x => x.LocalDate).Distinct().Count());
        Assert.All(occurrences.Where(x => x.Status == RecurringOccurrenceStatus.Generated), x => Assert.NotNull(x.ShiftId));
    }

    [Fact]
    public async Task GapResolutionAndSkipRemainVisibleAndIndependent()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("America/New_York", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var input = Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 3, 8), new TimeOnly(2, 30), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Sunday]);
        var resolvedSeriesId = await recurrence.CreateRecurringSeriesAsync(input, Coordinator, default);
        var skippedSeriesId = await recurrence.CreateRecurringSeriesAsync(input with { Title = "Skipped gap" }, Coordinator, default);

        var resolvedGap = await context.RecurringShiftOccurrences.SingleAsync(x => x.SeriesId == resolvedSeriesId && x.Status == RecurringOccurrenceStatus.NeedsReview);
        var skippedGap = await context.RecurringShiftOccurrences.SingleAsync(x => x.SeriesId == skippedSeriesId && x.Status == RecurringOccurrenceStatus.NeedsReview);
        await recurrence.ResolveOccurrenceGapAsync(resolvedGap.Id, resolvedGap.Version, new TimeOnly(3, 30), Coordinator, default);
        await recurrence.SkipOccurrenceGapAsync(skippedGap.Id, skippedGap.Version, "The service is closed for the transition.", Coordinator, default);

        await using var verification = _fixture.CreateContext();
        var resolved = await verification.RecurringShiftOccurrences.SingleAsync(x => x.Id == resolvedGap.Id);
        var skipped = await verification.RecurringShiftOccurrences.SingleAsync(x => x.Id == skippedGap.Id);
        Assert.Equal(RecurringOccurrenceStatus.Generated, resolved.Status);
        Assert.True(resolved.IsException);
        Assert.NotNull(resolved.ShiftId);
        Assert.Equal(RecurringOccurrenceStatus.Skipped, skipped.Status);
        Assert.True(skipped.IsException);
        Assert.Null(skipped.ShiftId);
        Assert.Equal(1, await verification.Shifts.CountAsync(x => x.RecurringOccurrenceId == resolved.Id && x.PublishedAtUtc == null));
    }

    [Fact]
    public async Task ManualExceptionDoesNotChangeAdjacentOccurrence()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrences = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .Take(3)
            .ToListAsync();
        var originalShift = await context.Shifts.SingleAsync(x => x.Id == occurrences[0].ShiftId);
        await recurrence.MarkOccurrenceExceptionAsync(occurrences[0].Id, Coordinator, "Changed only this occurrence.", default);

        var current = await recurrence.GetCurrentRevisionInputAsync(seriesId, default);
        var draft = current with
        {
            EffectiveLocalDate = occurrences[1].LocalDate,
            Title = "Following revision",
            LocalStartTime = new TimeOnly(10, 0)
        };
        var preview = await recurrence.PreviewRevisionAsync(seriesId, draft, default);
        await recurrence.ApplyRevisionAsync(
            seriesId,
            draft with { ExpectedClassification = preview.ExpectedClassification },
            Coordinator,
            default);

        await using var verification = _fixture.CreateContext();
        var rows = await verification.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .Take(2)
            .ToListAsync();
        var unchanged = await verification.Shifts.SingleAsync(x => x.Id == rows[0].ShiftId);
        var revised = await verification.Shifts.SingleAsync(x => x.Id == rows[1].ShiftId);
        Assert.True(rows[0].IsException);
        Assert.Equal(originalShift.Title, unchanged.Title);
        Assert.False(rows[1].IsException);
        Assert.Equal("Following revision", revised.Title);
        Assert.Equal(new TimeOnly(10, 0).ToTimeSpan(), revised.StartsAtUtc.TimeOfDay);
    }

    [Fact]
    public async Task RevisionDetachesProtectedOccurrenceAndUpdatesEligibleNeighbor()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrences = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .Take(4)
            .ToListAsync();
        var protectedShift = await context.Shifts.SingleAsync(x => x.Id == occurrences[2].ShiftId);
        var volunteer = Volunteer.Create("Protected Volunteer", "protected@example.org", null, FixedNow);
        context.Volunteers.Add(volunteer);
        context.Assignments.Add(Assignment.Create(
            protectedShift.Slots.Single(x => x.Kind == SlotKind.Primary).Id,
            protectedShift.Id,
            volunteer.Id,
            null,
            Coordinator,
            FixedNow));
        await context.SaveChangesAsync();
        var oldProtectedStart = protectedShift.StartsAtUtc;
        var current = await recurrence.GetCurrentRevisionInputAsync(seriesId, default);
        var draft = current with { EffectiveLocalDate = occurrences[1].LocalDate, LocalStartTime = new TimeOnly(10, 0) };
        var preview = await recurrence.PreviewRevisionAsync(seriesId, draft, default);
        var confirmed = draft with { ExpectedClassification = preview.ExpectedClassification };
        await recurrence.ApplyRevisionAsync(seriesId, confirmed, Coordinator, default);

        await using var verification = _fixture.CreateContext();
        var protectedOccurrence = await verification.RecurringShiftOccurrences.SingleAsync(x => x.Id == occurrences[2].Id);
        var eligibleOccurrence = await verification.RecurringShiftOccurrences.SingleAsync(x => x.Id == occurrences[1].Id);
        var protectedAfter = await verification.Shifts.SingleAsync(x => x.Id == protectedOccurrence.ShiftId);
        var eligibleAfter = await verification.Shifts.SingleAsync(x => x.Id == eligibleOccurrence.ShiftId);
        Assert.True(protectedOccurrence.IsException);
        Assert.Equal(oldProtectedStart, protectedAfter.StartsAtUtc);
        Assert.False(eligibleOccurrence.IsException);
        Assert.Equal(new TimeOnly(10, 0).ToTimeSpan(), eligibleAfter.StartsAtUtc.TimeOfDay);
    }

    [Fact]
    public async Task PublicationIsAllOrNoneAndReportsBlocker()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday, DayOfWeek.Thursday]),
            Coordinator,
            default);
        var occurrences = await context.RecurringShiftOccurrences.Where(x => x.SeriesId == seriesId).OrderBy(x => x.LocalDate).Take(2).ToListAsync();
        await recurrence.MarkOccurrenceExceptionAsync(occurrences[1].Id, Coordinator, "Hold for review.", default);
        var blockedPreview = await recurrence.PreviewPublicationAsync(seriesId, occurrences[0].LocalDate, occurrences[1].LocalDate, default);
        Assert.Contains(blockedPreview.Blockers, x => x.LocalDate == occurrences[1].LocalDate);
        await Assert.ThrowsAsync<DomainException>(() => recurrence.PublishRecurringOccurrencesAsync(
            seriesId,
            occurrences[0].LocalDate,
            occurrences[1].LocalDate,
            blockedPreview.ExpectedSeriesVersion,
            blockedPreview.ExpectedVersions,
            Coordinator,
            default));

        await using var blockedVerification = _fixture.CreateContext();
        Assert.All(
            await blockedVerification.Shifts.Where(x => x.RecurringOccurrenceId == occurrences[0].Id).ToListAsync(),
            x => Assert.Null(x.PublishedAtUtc));
    }

    [Fact]
    public async Task GroupZoneMismatchPausesGenerationUntilAdoption()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("America/New_York", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var before = await context.RecurringShiftOccurrences.CountAsync(x => x.SeriesId == seriesId);
        var currentSettings = await workflow.GetGroupSettingsAsync(default);
        await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", currentSettings!.Version, true, Coordinator, default);
        Assert.False(await recurrence.GenerateSeriesAsync(seriesId, default));
        Assert.Equal(before, await context.RecurringShiftOccurrences.CountAsync(x => x.SeriesId == seriesId));
        var detail = await recurrence.GetRecurringSeriesDetailAsync(seriesId, default);
        await recurrence.AdoptGroupZoneAsync(seriesId, detail.ExpectedSeriesVersion, Coordinator, default);

        await using var verification = _fixture.CreateContext();
        var revisions = await verification.RecurringShiftSeriesRevisions.Where(x => x.SeriesId == seriesId).OrderByDescending(x => x.RevisionNumber).ToListAsync();
        Assert.Equal("Etc/UTC", revisions[0].TimeZoneId);
        Assert.Equal(2, revisions.Count);
    }

    [Fact]
    public async Task StaleRevisionClassificationRollsBackWithoutNewRevision()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var rows = await context.RecurringShiftOccurrences.Where(x => x.SeriesId == seriesId).OrderBy(x => x.LocalDate).Take(2).ToListAsync();
        var current = await recurrence.GetCurrentRevisionInputAsync(seriesId, default);
        var draft = current with { EffectiveLocalDate = rows[1].LocalDate, LocalStartTime = new TimeOnly(10, 0) };
        var preview = await recurrence.PreviewRevisionAsync(seriesId, draft, default);
        await recurrence.MarkOccurrenceExceptionAsync(rows[1].Id, Coordinator, "Changed while review was open.", default);

        await Assert.ThrowsAsync<DomainException>(() => recurrence.ApplyRevisionAsync(
            seriesId,
            draft with { ExpectedClassification = preview.ExpectedClassification },
            Coordinator,
            default));

        await using var verification = _fixture.CreateContext();
        Assert.Equal(1, await verification.RecurringShiftSeriesRevisions.CountAsync(x => x.SeriesId == seriesId));
        Assert.True(await verification.RecurringShiftOccurrences.Where(x => x.Id == rows[1].Id).Select(x => x.IsException).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentTopUpKeepsOneOccurrencePerLocalDate()
    {
        await _fixture.ResetAsync();
        await using var seedContext = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(seedContext, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var seedRecurrence = new RecurringShiftService(new EfWorkflowStore(seedContext), new EfWorkflowStore(seedContext), clock);
        var seriesId = await seedRecurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);

        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var first = new RecurringShiftService(new EfWorkflowStore(firstContext), new EfWorkflowStore(firstContext), clock);
        var second = new RecurringShiftService(new EfWorkflowStore(secondContext), new EfWorkflowStore(secondContext), clock);
        await Task.WhenAll(
            first.GenerateSeriesThroughAsync(seriesId, new DateOnly(2026, 4, 1), default),
            second.GenerateSeriesThroughAsync(seriesId, new DateOnly(2026, 4, 1), default));

        await using var verification = _fixture.CreateContext();
        var dates = await verification.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .Select(x => x.LocalDate)
            .ToListAsync();
        Assert.Equal(dates.Count, dates.Distinct().Count());
    }

    [Fact]
    public async Task GeneratorWaitsForHeldSeriesLockAcrossContexts()
    {
        await _fixture.ResetAsync();
        await using var seedContext = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(seedContext, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var seedRecurrence = new RecurringShiftService(new EfWorkflowStore(seedContext), new EfWorkflowStore(seedContext), clock);
        var seriesId = await seedRecurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockRecurringSeriesAsync(seriesId, token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var generatorContext = _fixture.CreateContext();
        var generatorTask = new RecurringShiftService(
            new EfWorkflowStore(generatorContext),
            new EfWorkflowStore(generatorContext),
            clock).GenerateSeriesThroughAsync(seriesId, new DateOnly(2026, 4, 1), default);
        await ScheduleTestHelpers.AssertBlockedAsync(generatorTask);

        releaseLock.SetResult(true);
        await lockTask;
        Assert.True(await generatorTask);
    }

    [Fact]
    public async Task HeldSlotLockReclassifiesNewRequestAndRollsBackRevision()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrences = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .Take(3)
            .ToListAsync();
        var targetShift = await context.Shifts.SingleAsync(x => x.Id == occurrences[1].ShiftId);
        var targetSlotId = targetShift.Slots.Single(x => x.Kind == SlotKind.Primary).Id;
        var current = await recurrence.GetCurrentRevisionInputAsync(seriesId, default);
        var draft = current with { EffectiveLocalDate = occurrences[1].LocalDate, Title = "Rejected correction" };
        var preview = await recurrence.PreviewRevisionAsync(seriesId, draft, default);

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockSlotAsync(targetSlotId, token);
                var volunteer = Volunteer.Create("Concurrent requester", "concurrent-requester@example.org", null, FixedNow);
                lockContext.Volunteers.Add(volunteer);
                lockContext.ShiftRequests.Add(ShiftRequest.Create(targetSlotId, volunteer.Id, FixedNow));
                await lockContext.SaveChangesAsync(token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var applyContext = _fixture.CreateContext();
        var applyTask = new RecurringShiftService(
            new EfWorkflowStore(applyContext),
            new EfWorkflowStore(applyContext),
            clock).ApplyRevisionAsync(
                seriesId,
                draft with { ExpectedClassification = preview.ExpectedClassification },
                Coordinator,
                default);
        await ScheduleTestHelpers.AssertBlockedAsync(applyTask);

        releaseLock.SetResult(true);
        await lockTask;
        await Assert.ThrowsAsync<DomainException>(() => applyTask);

        await using var verification = _fixture.CreateContext();
        Assert.Equal(1, await verification.RecurringShiftSeriesRevisions.CountAsync(x => x.SeriesId == seriesId));
        Assert.True(await verification.ShiftRequests.AnyAsync(x => x.ShiftSlotId == targetSlotId && x.Status == RequestStatus.Pending));
        Assert.False(await verification.RecurringShiftOccurrences.Where(x => x.Id == occurrences[1].Id).Select(x => x.IsException).SingleAsync());
    }

    [Fact]
    public async Task HeldSlotLockReclassifiesNewAssignmentAndRollsBackRevision()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrences = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .Take(3)
            .ToListAsync();
        var targetShift = await context.Shifts.SingleAsync(x => x.Id == occurrences[1].ShiftId);
        var targetSlotId = targetShift.Slots.Single(x => x.Kind == SlotKind.Primary).Id;
        var current = await recurrence.GetCurrentRevisionInputAsync(seriesId, default);
        var draft = current with { EffectiveLocalDate = occurrences[1].LocalDate, Title = "Rejected assignment correction" };
        var preview = await recurrence.PreviewRevisionAsync(seriesId, draft, default);

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockSlotAsync(targetSlotId, token);
                var volunteer = Volunteer.Create("Concurrent assignee", "concurrent-assignee@example.org", null, FixedNow);
                lockContext.Volunteers.Add(volunteer);
                lockContext.Assignments.Add(Assignment.Create(
                    targetSlotId,
                    targetShift.Id,
                    volunteer.Id,
                    null,
                    Coordinator,
                    FixedNow));
                await lockContext.SaveChangesAsync(token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var applyContext = _fixture.CreateContext();
        var applyTask = new RecurringShiftService(
            new EfWorkflowStore(applyContext),
            new EfWorkflowStore(applyContext),
            clock).ApplyRevisionAsync(
                seriesId,
                draft with { ExpectedClassification = preview.ExpectedClassification },
                Coordinator,
                default);
        await ScheduleTestHelpers.AssertBlockedAsync(applyTask);

        releaseLock.SetResult(true);
        await lockTask;
        await Assert.ThrowsAsync<DomainException>(() => applyTask);

        await using var verification = _fixture.CreateContext();
        Assert.Equal(1, await verification.RecurringShiftSeriesRevisions.CountAsync(x => x.SeriesId == seriesId));
        Assert.True(await verification.Assignments.AnyAsync(x =>
            x.ShiftSlotId == targetSlotId &&
            (x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed)));
    }

    [Fact]
    public async Task HeldBackupSlotLockSerializesOneOccurrenceEdit()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrence = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .FirstAsync();
        var shift = await context.Shifts.SingleAsync(x => x.Id == occurrence.ShiftId);
        var backupSlotId = shift.Slots.Single(x => x.Kind == SlotKind.Backup).Id;

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockSlotAsync(backupSlotId, token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var editContext = _fixture.CreateContext();
        var editTask = ScheduleTestHelpers.CreateService(editContext, clock).EditShiftAsync(
            shift.Id,
            shift.Version,
            "Edited occurrence",
            shift.Location,
            shift.Notes,
            shift.VolunteerInstructions,
            ScheduleTestHelpers.ForInstantRange(
                new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero),
                settings),
            0,
            Coordinator,
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(editTask);

        releaseLock.SetResult(true);
        await lockTask;
        await editTask;

        await using var verification = _fixture.CreateContext();
        Assert.True(await verification.RecurringShiftOccurrences.Where(x => x.Id == occurrence.Id).Select(x => x.IsException).SingleAsync());
    }

    [Fact]
    public async Task HeldPrimarySlotLockSerializesOneOccurrenceDeactivation()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrence = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .FirstAsync();
        var shift = await context.Shifts.SingleAsync(x => x.Id == occurrence.ShiftId);
        var primarySlotId = shift.Slots.Single(x => x.Kind == SlotKind.Primary).Id;

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockSlotAsync(primarySlotId, token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var deactivateContext = _fixture.CreateContext();
        var deactivateTask = ScheduleTestHelpers.CreateService(deactivateContext, clock).DeactivateShiftAsync(
            shift.Id,
            shift.Version,
            Coordinator,
            default);
        await ScheduleTestHelpers.AssertBlockedAsync(deactivateTask);

        releaseLock.SetResult(true);
        await lockTask;
        await deactivateTask;

        await using var verification = _fixture.CreateContext();
        Assert.False(await verification.Shifts.Where(x => x.Id == shift.Id).Select(x => x.IsActive).SingleAsync());
        Assert.True(await verification.RecurringShiftOccurrences.Where(x => x.Id == occurrence.Id).Select(x => x.IsException).SingleAsync());
    }

    [Fact]
    public async Task HeldSlotLockSerializesPublication()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrence = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .FirstAsync();
        var shift = await context.Shifts.SingleAsync(x => x.Id == occurrence.ShiftId);
        var slotId = shift.Slots.Single(x => x.Kind == SlotKind.Primary).Id;
        var publication = await recurrence.PreviewPublicationAsync(seriesId, occurrence.LocalDate, occurrence.LocalDate, default);

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockSlotAsync(slotId, token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var publishContext = _fixture.CreateContext();
        var publishTask = new RecurringShiftService(
            new EfWorkflowStore(publishContext),
            new EfWorkflowStore(publishContext),
            clock).PublishRecurringOccurrencesAsync(
                seriesId,
                occurrence.LocalDate,
                occurrence.LocalDate,
                publication.ExpectedSeriesVersion,
                publication.ExpectedVersions,
                Coordinator,
                default);
        await ScheduleTestHelpers.AssertBlockedAsync(publishTask);

        releaseLock.SetResult(true);
        await lockTask;
        var result = await publishTask;
        Assert.Equal(1, result.ChangedCount);

        await using var verification = _fixture.CreateContext();
        Assert.NotNull(await verification.Shifts.Where(x => x.Id == shift.Id).Select(x => x.PublishedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task HeldGroupSettingsLockRejectsZoneAdoptionAfterZoneChange()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var initialSettings = await workflow.ConfigureGroupTimeZoneAsync("America/New_York", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(initialSettings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var currentSettings = await workflow.GetGroupSettingsAsync(default);
        await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", currentSettings!.Version, true, Coordinator, default);
        var detail = await recurrence.GetRecurringSeriesDetailAsync(seriesId, default);

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockGroupSettingsAsync(token);
                var settings = await lockContext.GroupSettings.SingleAsync(token);
                settings.Configure("Asia/Tokyo");
                await lockContext.SaveChangesAsync(token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var adoptionContext = _fixture.CreateContext();
        var adoptionTask = new RecurringShiftService(
            new EfWorkflowStore(adoptionContext),
            new EfWorkflowStore(adoptionContext),
            clock).AdoptGroupZoneAsync(seriesId, detail.ExpectedSeriesVersion, Coordinator, default);
        await ScheduleTestHelpers.AssertBlockedAsync(adoptionTask);

        releaseLock.SetResult(true);
        await lockTask;
        await Assert.ThrowsAsync<DomainException>(() => adoptionTask);

        await using var verification = _fixture.CreateContext();
        Assert.Equal(1, await verification.RecurringShiftSeriesRevisions.CountAsync(x => x.SeriesId == seriesId));
        Assert.Equal("Asia/Tokyo", await verification.GroupSettings.Select(x => x.TimeZoneId).SingleAsync());
    }

    [Fact]
    public async Task HeldLockPublicationRevalidatesEveryRowAndPublishesNone()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday, DayOfWeek.Thursday]),
            Coordinator,
            default);
        var occurrences = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .Take(2)
            .ToListAsync();
        var publication = await recurrence.PreviewPublicationAsync(
            seriesId,
            occurrences[0].LocalDate,
            occurrences[1].LocalDate,
            default);
        var secondShift = await context.Shifts.SingleAsync(x => x.Id == occurrences[1].ShiftId);
        var secondSlotId = secondShift.Slots.Single(x => x.Kind == SlotKind.Primary).Id;

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockSlotAsync(secondSlotId, token);
                var heldShift = await lockContext.Shifts.SingleAsync(x => x.Id == secondShift.Id, token);
                heldShift.Deactivate();
                await lockContext.SaveChangesAsync(token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var publishContext = _fixture.CreateContext();
        var publishTask = new RecurringShiftService(
            new EfWorkflowStore(publishContext),
            new EfWorkflowStore(publishContext),
            clock).PublishRecurringOccurrencesAsync(
                seriesId,
                occurrences[0].LocalDate,
                occurrences[1].LocalDate,
                publication.ExpectedSeriesVersion,
                publication.ExpectedVersions,
                Coordinator,
                default);
        await ScheduleTestHelpers.AssertBlockedAsync(publishTask);

        releaseLock.SetResult(true);
        await lockTask;
        await Assert.ThrowsAsync<DomainException>(() => publishTask);

        await using var verification = _fixture.CreateContext();
        Assert.All(
            await verification.Shifts
                .Where(x => x.Id == occurrences[0].ShiftId || x.Id == occurrences[1].ShiftId)
                .Select(x => x.PublishedAtUtc)
                .ToListAsync(),
            value => Assert.Null(value));
    }

    [Fact]
    public async Task HeldCorrectionLockRollsBackWhenOccurrenceClassificationChanges()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var occurrences = await context.RecurringShiftOccurrences
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.LocalDate)
            .Take(3)
            .ToListAsync();
        var targetShift = await context.Shifts.SingleAsync(x => x.Id == occurrences[1].ShiftId);
        var targetSlotId = targetShift.Slots.Single(x => x.Kind == SlotKind.Primary).Id;
        var current = await recurrence.GetCurrentRevisionInputAsync(seriesId, default);
        var draft = current with { EffectiveLocalDate = occurrences[1].LocalDate, Title = "Rolled back correction" };
        var preview = await recurrence.PreviewRevisionAsync(seriesId, draft, default);

        await using var lockContext = _fixture.CreateContext();
        var lockStore = new EfWorkflowStore(lockContext);
        var lockHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = lockStore.ExecuteInTransactionAsync(
            async token =>
            {
                await lockStore.LockSlotAsync(targetSlotId, token);
                var occurrence = await lockContext.RecurringShiftOccurrences.SingleAsync(x => x.Id == occurrences[1].Id, token);
                occurrence.MarkException("Concurrent one-occurrence edit.");
                await lockContext.SaveChangesAsync(token);
                lockHeld.SetResult(true);
                await releaseLock.Task.WaitAsync(token);
                return true;
            },
            default);
        await lockHeld.Task;

        await using var applyContext = _fixture.CreateContext();
        var applyTask = new RecurringShiftService(
            new EfWorkflowStore(applyContext),
            new EfWorkflowStore(applyContext),
            clock).ApplyRevisionAsync(
                seriesId,
                draft with { ExpectedClassification = preview.ExpectedClassification },
                Coordinator,
                default);
        await ScheduleTestHelpers.AssertBlockedAsync(applyTask);

        releaseLock.SetResult(true);
        await lockTask;
        await Assert.ThrowsAsync<DomainException>(() => applyTask);

        await using var verification = _fixture.CreateContext();
        Assert.Equal(1, await verification.RecurringShiftSeriesRevisions.CountAsync(x => x.SeriesId == seriesId));
        Assert.True(await verification.RecurringShiftOccurrences.Where(x => x.Id == occurrences[1].Id).Select(x => x.IsException).SingleAsync());
    }

    [Fact]
    public async Task RevisionEffectiveDateMustBeEligibleGeneratedBoundary()
    {
        await _fixture.ResetAsync();
        await using var context = _fixture.CreateContext();
        var clock = new ScheduleTestHelpers.FixedClock(FixedNow);
        var workflow = ScheduleTestHelpers.CreateService(context, clock);
        var settings = await workflow.ConfigureGroupTimeZoneAsync("Etc/UTC", null, false, Coordinator, default);
        var recurrence = new RecurringShiftService(new EfWorkflowStore(context), new EfWorkflowStore(context), clock);
        var seriesId = await recurrence.CreateRecurringSeriesAsync(
            Input(settings, RecurrenceKind.Weekly, new DateOnly(2026, 1, 5), new TimeOnly(9, 0), horizonWeeks: 4, interval: 1, weekdays: [DayOfWeek.Monday]),
            Coordinator,
            default);
        var current = await recurrence.GetCurrentRevisionInputAsync(seriesId, default);

        await Assert.ThrowsAsync<DomainException>(() =>
            recurrence.PreviewRevisionAsync(seriesId, current with { EffectiveLocalDate = new DateOnly(2026, 1, 1) }, default));
        await Assert.ThrowsAsync<DomainException>(() =>
            recurrence.PreviewRevisionAsync(seriesId, current with { EffectiveLocalDate = new DateOnly(2026, 1, 6) }, default));
        await Assert.ThrowsAsync<DomainException>(() =>
            recurrence.PreviewRevisionAsync(seriesId, current with { EffectiveLocalDate = new DateOnly(2026, 1, 30) }, default));
    }

    private static RecurringSeriesInput Input(
        GroupSettingsDto settings,
        RecurrenceKind kind,
        DateOnly anchor,
        TimeOnly start,
        int horizonWeeks,
        int interval,
        IReadOnlyCollection<DayOfWeek>? weekdays = null) =>
        new(
            "Recurring test series",
            "Hall",
            "Use the side door.",
            "Internal note",
            kind,
            interval,
            weekdays ?? [],
            anchor,
            start,
            120,
            1,
            horizonWeeks,
            AmbiguousTimeChoice.FirstOccurrence,
            settings.Version);
}
