using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Web.Presentation;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class GroupTimeFormatterTests
{
    [Fact]
    public void TokyoUsesStableIanaZoneAndOffsetTextAcrossPlatforms()
    {
        var formatter = new GroupTimeFormatter();
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = formatter.FormatLocal(instant, "Asia/Tokyo");

        Assert.Equal("Thursday, January 1, 2026, 9:00 AM Asia/Tokyo (UTC+09:00)", result);
        Assert.DoesNotContain("Standard Time", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Tokyo Standard", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticInstantAndDurationRetainSecondsAndFractions()
    {
        var formatter = new GroupTimeFormatter();
        var startsAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);
        var endsAtUtc = startsAtUtc.AddSeconds(30);
        var commitment = new CommitmentDto(
            Guid.NewGuid(),
            null,
            "Short commitment",
            startsAtUtc,
            endsAtUtc,
            "Etc/UTC",
            null,
            "Primary",
            null);
        Assert.Equal(
            "Thursday, January 1, 2026, 12:00:00.1234567 AM UTC (UTC+00:00)",
            formatter.FormatLocal(startsAtUtc, "Etc/UTC"));

        Assert.Equal("2026-01-01T00:00:00.1234567Z", formatter.UtcDateTime(startsAtUtc));
        Assert.Equal("2026-01-01T00:00:30.1234567Z", formatter.UtcDateTime(endsAtUtc));
        Assert.Equal("30 seconds", formatter.FormatDuration(commitment));

        var fractionalCommitment = commitment with { EndsAtUtc = startsAtUtc.AddTicks(305_000_000) };
        Assert.Equal("30.5 seconds", formatter.FormatDuration(fractionalCommitment));
    }
}
