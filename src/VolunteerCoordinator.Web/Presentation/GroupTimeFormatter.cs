using System.Globalization;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Time;

namespace VolunteerCoordinator.Web.Presentation;

public sealed class GroupTimeFormatter
{
    public string FormatLocal(DateTimeOffset instantUtc, string ianaTimeZoneId)
    {
        var (normalizedId, timeZone) = Resolve(ianaTimeZoneId);
        var local = TimeZoneInfo.ConvertTime(instantUtc, timeZone);
        var zoneLabel = TimeZoneLabels.StableLabel(normalizedId, timeZone, instantUtc);
        var localText = local.Ticks % TimeSpan.TicksPerMinute == 0
            ? local.ToString("dddd, MMMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture)
            : local.Ticks % TimeSpan.TicksPerSecond == 0
                ? local.ToString("dddd, MMMM d, yyyy, h:mm:ss tt", CultureInfo.InvariantCulture)
                : local.ToString("dddd, MMMM d, yyyy, h:mm:ss.fffffff tt", CultureInfo.InvariantCulture);
        return $"{localText} {zoneLabel} ({TimeZoneLabels.OffsetText(local.Offset)})";
    }

    public string FormatDuration(CommitmentDto commitment)
    {
        var duration = commitment.EndsAtUtc - commitment.StartsAtUtc;
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentException("Commitment end must be after its start.", nameof(commitment));
        }

        var parts = new List<string>(4);
        AddUnit(parts, duration.Days, "day");
        AddUnit(parts, duration.Hours, "hour");
        AddUnit(parts, duration.Minutes, "minute");

        var seconds = duration.Seconds;
        var fractionalTicks = duration.Ticks % TimeSpan.TicksPerSecond;
        if (seconds != 0 || fractionalTicks != 0 || parts.Count == 0)
        {
            var secondsText = fractionalTicks == 0
                ? seconds.ToString(CultureInfo.InvariantCulture)
                : $"{seconds}.{fractionalTicks:D7}".TrimEnd('0');
            parts.Add($"{secondsText} second{(secondsText == "1" ? string.Empty : "s")}");
        }

        return string.Join(" ", parts);
    }

    public string FormatTimeZone(CommitmentDto commitment) =>
        FriendlyTimeZone(commitment.GroupTimeZoneId);

    public string FriendlyTimeZone(string ianaTimeZoneId) =>
        ianaTimeZoneId switch
        {
            "America/New_York" => "Eastern time",
            "America/Chicago" => "Central time",
            "America/Denver" => "Mountain time",
            "America/Los_Angeles" => "Pacific time",
            "America/Phoenix" => "Arizona time",
            "Europe/London" => "United Kingdom time",
            "Europe/Berlin" or "Europe/Paris" => "Central European time",
            "Australia/Sydney" or "Australia/Melbourne" => "Eastern Australia time",
            "Etc/UTC" or "Etc/GMT" => "Coordinated universal time",
            _ => "Local group time"
        };

    public string UtcDateTime(DateTimeOffset instantUtc)
    {
        var utc = instantUtc.UtcDateTime;
        return utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static (string Id, TimeZoneInfo Zone) Resolve(string ianaTimeZoneId)
    {
        if (!TimeZoneLabels.TryGetIanaZone(ianaTimeZoneId, out var normalizedId, out var timeZone))
        {
            throw new InvalidOperationException("The configured group time zone is unavailable.");
        }

        return (normalizedId, timeZone);
    }

    private static void AddUnit(ICollection<string> parts, int value, string unit)
    {
        if (value != 0)
        {
            parts.Add($"{value} {unit}{(value == 1 ? string.Empty : "s")}");
        }
    }
}
