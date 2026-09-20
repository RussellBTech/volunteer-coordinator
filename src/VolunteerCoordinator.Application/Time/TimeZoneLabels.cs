using System.Globalization;

namespace VolunteerCoordinator.Application.Time;

public static class TimeZoneLabels
{
    public static bool TryGetIanaZone(
        string? value,
        out string normalizedId,
        out TimeZoneInfo timeZone)
    {
        normalizedId = value?.Trim() ?? string.Empty;
        timeZone = null!;
        if (normalizedId.Length == 0 || normalizedId.Length > 200)
        {
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(normalizedId);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        return timeZone.HasIanaId;
    }

    public static string StableLabel(
        string ianaTimeZoneId,
        TimeZoneInfo timeZone,
        DateTimeOffset instantUtc)
    {
        var isDaylight = timeZone.IsDaylightSavingTime(instantUtc);
        var explicitId = ianaTimeZoneId switch
        {
            "America/New_York" => isDaylight ? "EDT" : "EST",
            "America/Chicago" => isDaylight ? "CDT" : "CST",
            "America/Denver" => isDaylight ? "MDT" : "MST",
            "America/Los_Angeles" => isDaylight ? "PDT" : "PST",
            "America/Phoenix" => "MST",
            "Europe/London" => isDaylight ? "BST" : "GMT",
            "Europe/Berlin" => isDaylight ? "CEST" : "CET",
            "Europe/Paris" => isDaylight ? "CEST" : "CET",
            "Australia/Sydney" => isDaylight ? "AEDT" : "AEST",
            "Australia/Melbourne" => isDaylight ? "AEDT" : "AEST",
            "Etc/UTC" => "UTC",
            "Etc/GMT" => "GMT",
            _ => string.Empty
        };

        // IANA does not define a universal abbreviation for every zone. The
        // identifier is stable across operating systems; FormatLocal adds the
        // numeric offset for an unambiguous representation.
        return explicitId.Length > 0 ? explicitId : ianaTimeZoneId;
    }

    public static string OffsetValue(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        return $"{sign}{(int)absolute.TotalHours:00}:{absolute.Minutes:00}";
    }

    public static string OffsetText(TimeSpan offset) => $"UTC{OffsetValue(offset)}";
    public static string LocalCandidateLabel(
        LocalScheduleCandidateParts candidate,
        string ianaTimeZoneId,
        TimeZoneInfo timeZone) =>
        $"{candidate.LocalTime.ToString("h:mm tt", CultureInfo.InvariantCulture)} " +
        $"{StableLabel(ianaTimeZoneId, timeZone, candidate.UtcInstant)} ({OffsetText(candidate.UtcOffset)})";
}
