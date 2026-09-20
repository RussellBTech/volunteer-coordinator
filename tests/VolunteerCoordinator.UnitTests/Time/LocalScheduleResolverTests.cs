using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Time;
using VolunteerCoordinator.Domain.Settings;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Time;

public sealed class LocalScheduleResolverTests
{
    private static readonly GroupSettings NewYork = GroupSettings.Create("America/New_York");

    [Fact]
    public void SpringGapIsRejectedWithoutShiftingTheWallTime()
    {
        var input = new LocalScheduleInput(
            new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Local),
            new DateTime(2026, 3, 8, 4, 0, 0, DateTimeKind.Local),
            null,
            null,
            0);

        var result = LocalScheduleResolver.Resolve(NewYork, input);

        Assert.Contains(LocalScheduleResolver.InvalidTimeMessage, result.Errors);
        Assert.Null(result.StartsAtUtc);
        Assert.Equal(DateTimeKind.Unspecified, result.StartsAtLocal.Kind);
    }

    [Fact]
    public void AutumnOverlapReturnsOrderedCandidatesUntilAnOffsetIsSelected()
    {
        var input = new LocalScheduleInput(
            new DateTime(2026, 11, 1, 1, 30, 0),
            new DateTime(2026, 11, 1, 2, 30, 0),
            null,
            null,
            0);

        var unresolved = LocalScheduleResolver.Resolve(NewYork, input);

        Assert.False(unresolved.IsComplete);
        Assert.Equal(2, unresolved.StartCandidates.Count);
        Assert.Equal(TimeSpan.FromHours(-4), unresolved.StartCandidates[0].UtcOffset);
        Assert.Equal(TimeSpan.FromHours(-5), unresolved.StartCandidates[1].UtcOffset);
        Assert.Contains("EDT", unresolved.StartCandidates[0].ZoneLabel);
        Assert.Contains("EST", unresolved.StartCandidates[1].ZoneLabel);

        var resolved = LocalScheduleResolver.Resolve(
            NewYork,
            input with { StartsAtOffset = TimeSpan.FromHours(-5) });

        Assert.True(resolved.IsComplete);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), resolved.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero), resolved.EndsAtUtc);
    }

    [Fact]
    public void AutumnOverlapAcceptsTheEarlierOffsetChoice()
    {
        var input = new LocalScheduleInput(
            new DateTime(2026, 11, 1, 1, 30, 0),
            new DateTime(2026, 11, 1, 2, 30, 0),
            TimeSpan.FromHours(-4),
            null,
            0);

        var result = LocalScheduleResolver.Resolve(NewYork, input);

        Assert.True(result.IsComplete);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), result.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero), result.EndsAtUtc);
    }

    [Fact]
    public void InvalidIanaZoneIsRejectedWithoutResolvingAnInstant()
    {
        var result = LocalScheduleResolver.Resolve(
            GroupSettings.Create("Not/AZone"),
            new LocalScheduleInput(
                new DateTime(2026, 10, 3, 10, 0, 0),
                new DateTime(2026, 10, 3, 11, 0, 0),
                null,
                null,
                0));

        Assert.False(result.IsComplete);
        Assert.Contains("unavailable", Assert.Single(result.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.StartsAtUtc);
        Assert.Null(result.EndsAtUtc);
    }

    [Fact]
    public void StaleOverlapOffsetIsRejected()
    {
        var input = new LocalScheduleInput(
            new DateTime(2026, 11, 1, 1, 30, 0),
            new DateTime(2026, 11, 1, 3, 0, 0),
            TimeSpan.FromHours(-7),
            null,
            0);

        var result = LocalScheduleResolver.Resolve(NewYork, input);

        Assert.Contains(result.Errors, error => error.Contains("no longer valid", StringComparison.Ordinal));
        Assert.Null(result.StartsAtUtc);
    }

    [Fact]
    public void ElapsedDurationUsesUtcAcrossTheAutumnTransition()
    {
        var input = new LocalScheduleInput(
            new DateTime(2026, 11, 1, 1, 30, 0),
            new DateTime(2026, 11, 1, 1, 45, 0),
            TimeSpan.FromHours(-4),
            TimeSpan.FromHours(-5),
            0);

        var result = LocalScheduleResolver.Resolve(NewYork, input);

        Assert.True(result.IsComplete);
        Assert.Equal(TimeSpan.FromMinutes(75), result.EndsAtUtc!.Value - result.StartsAtUtc!.Value);
    }

    [Fact]
    public void NoDstZoneResolvesOrdinaryLocalInputDirectly()
    {
        var settings = GroupSettings.Create("Asia/Tokyo");
        var result = LocalScheduleResolver.Resolve(
            settings,
            new LocalScheduleInput(
                new DateTime(2026, 12, 1, 23, 30, 0),
                new DateTime(2026, 12, 2, 1, 0, 0),
                null,
                null,
                0));

        Assert.True(result.IsComplete);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 14, 30, 0, TimeSpan.Zero), result.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 16, 0, 0, TimeSpan.Zero), result.EndsAtUtc);
    }
}
