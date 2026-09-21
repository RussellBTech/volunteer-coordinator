using VolunteerCoordinator.Application.Time;
using VolunteerCoordinator.Domain.Schedules;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Schedules;

public sealed class RecurringScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DailyIntervalEnumeratesOnlyAnchoredCalendarDays()
    {
        var revision = CreateRevision(
            RecurrenceKind.Daily,
            interval: 2,
            weeklyDays: DayOfWeekMask.None,
            anchor: new DateOnly(2026, 3, 1));

        var dates = RecurringCalendar.EnumerateDates(
            revision,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 8));

        Assert.Equal(
            [
                new DateOnly(2026, 3, 1),
                new DateOnly(2026, 3, 3),
                new DateOnly(2026, 3, 5),
                new DateOnly(2026, 3, 7)
            ],
            dates);
    }

    [Fact]
    public void WeeklyIntervalHonorsSelectedWeekdaysAndAnchorWeek()
    {
        var revision = CreateRevision(
            RecurrenceKind.Weekly,
            interval: 2,
            weeklyDays: DayOfWeekMask.Monday | DayOfWeekMask.Thursday,
            anchor: new DateOnly(2026, 3, 2));

        var dates = RecurringCalendar.EnumerateDates(
            revision,
            new DateOnly(2026, 3, 2),
            new DateOnly(2026, 3, 31));

        Assert.Equal(
            [
                new DateOnly(2026, 3, 2),
                new DateOnly(2026, 3, 5),
                new DateOnly(2026, 3, 16),
                new DateOnly(2026, 3, 19),
                new DateOnly(2026, 3, 30)
            ],
            dates);
    }

    [Fact]
    public void SpringGapRemainsUnresolved()
    {
        var revision = CreateRevision(
            RecurrenceKind.Daily,
            interval: 1,
            weeklyDays: DayOfWeekMask.None,
            anchor: new DateOnly(2026, 3, 8),
            start: new TimeOnly(2, 30));

        var resolution = RecurringLocalTimeResolver.Resolve(
            revision.TimeZoneId,
            revision.AnchorLocalDate,
            revision.LocalStartTime,
            revision.AmbiguousTimeChoice);

        Assert.True(resolution.IsGap);
        Assert.Null(resolution.StartsAtUtc);
    }

    [Theory]
    [InlineData(AmbiguousTimeChoice.FirstOccurrence, 5)]
    [InlineData(AmbiguousTimeChoice.SecondOccurrence, 6)]
    public void FallOverlapUsesStoredChoice(AmbiguousTimeChoice choice, int expectedUtcHour)
    {
        var revision = CreateRevision(
            RecurrenceKind.Daily,
            interval: 1,
            weeklyDays: DayOfWeekMask.None,
            anchor: new DateOnly(2026, 11, 1),
            start: new TimeOnly(1, 30),
            choice: choice);

        var resolution = RecurringLocalTimeResolver.Resolve(
            revision.TimeZoneId,
            revision.AnchorLocalDate,
            revision.LocalStartTime,
            revision.AmbiguousTimeChoice);

        Assert.Equal(expectedUtcHour, resolution.StartsAtUtc!.Value.Hour);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Fact]
    public void ElapsedDurationCrossesDstUsingUtcInstants()
    {
        var revision = CreateRevision(
            RecurrenceKind.Daily,
            interval: 1,
            weeklyDays: DayOfWeekMask.None,
            anchor: new DateOnly(2026, 11, 1),
            start: new TimeOnly(1, 30));
        var resolution = RecurringLocalTimeResolver.Resolve(
            revision.TimeZoneId,
            revision.AnchorLocalDate,
            revision.LocalStartTime,
            AmbiguousTimeChoice.FirstOccurrence);

        var endUtc = resolution.StartsAtUtc!.Value.AddMinutes(revision.DurationMinutes);
        var localEnd = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(endUtc, revision.TimeZoneId);

        Assert.Equal(new TimeOnly(2, 30), TimeOnly.FromDateTime(localEnd.DateTime));
    }

    [Fact]
    public void NoDstZoneResolvesOrdinaryLocalStart()
    {
        var revision = RecurringShiftSeriesRevision.Create(
            Guid.NewGuid(),
            1,
            new DateOnly(2026, 1, 5),
            "UTC series",
            null,
            null,
            null,
            RecurrenceKind.Daily,
            1,
            DayOfWeekMask.None,
            new DateOnly(2026, 1, 5),
            new TimeOnly(9, 0),
            60,
            0,
            4,
            "Etc/UTC",
            AmbiguousTimeChoice.FirstOccurrence,
            "coordinator@example.org",
            Now);

        var resolution = RecurringLocalTimeResolver.Resolve(
            revision.TimeZoneId,
            revision.AnchorLocalDate,
            revision.LocalStartTime,
            revision.AmbiguousTimeChoice);

        Assert.False(resolution.IsGap);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero), resolution.StartsAtUtc);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(26)]
    public void HorizonBoundariesAreAccepted(int horizonWeeks)
    {
        var revision = CreateRevision(
            RecurrenceKind.Daily,
            interval: 1,
            weeklyDays: DayOfWeekMask.None,
            anchor: new DateOnly(2026, 1, 1));

        Assert.Equal(12, revision.HorizonWeeks);
        Assert.NotNull(RecurringShiftSeriesRevision.Create(
            Guid.NewGuid(),
            1,
            revision.AnchorLocalDate,
            revision.Title,
            revision.Location,
            revision.VolunteerInstructions,
            revision.InternalCoordinatorNotes,
            revision.RecurrenceKind,
            revision.Interval,
            revision.WeeklyDays,
            revision.AnchorLocalDate,
            revision.LocalStartTime,
            revision.DurationMinutes,
            revision.BackupSlotCount,
            horizonWeeks,
            revision.TimeZoneId,
            revision.AmbiguousTimeChoice,
            revision.CreatedByCoordinator,
            Now));
    }

    private static RecurringShiftSeriesRevision CreateRevision(
        RecurrenceKind kind,
        int interval,
        DayOfWeekMask weeklyDays,
        DateOnly anchor,
        TimeOnly? start = null,
        AmbiguousTimeChoice choice = AmbiguousTimeChoice.FirstOccurrence) =>
        RecurringShiftSeriesRevision.Create(
            Guid.NewGuid(),
            1,
            anchor,
            "Test series",
            "Hall",
            "Instructions",
            "Private",
            kind,
            interval,
            weeklyDays,
            anchor,
            start ?? new TimeOnly(9, 0),
            120,
            1,
            12,
            "America/New_York",
            choice,
            "coordinator@example.org",
            Now);
}
