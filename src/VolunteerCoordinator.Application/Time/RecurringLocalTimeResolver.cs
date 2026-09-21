using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Time;

public sealed record RecurringLocalStartCandidate(
    DateTimeOffset UtcInstant,
    TimeSpan UtcOffset,
    string ZoneLabel);

public sealed record RecurringLocalStartResolution(
    DateOnly LocalDate,
    TimeOnly LocalStartTime,
    bool IsGap,
    DateTimeOffset? StartsAtUtc,
    TimeSpan? SelectedOffset,
    IReadOnlyList<RecurringLocalStartCandidate> Candidates)
{
    public bool IsResolved => !IsGap && StartsAtUtc.HasValue;
}

public static class RecurringLocalTimeResolver
{
    public static RecurringLocalStartResolution Resolve(
        string timeZoneId,
        DateOnly localDate,
        TimeOnly localStartTime,
        AmbiguousTimeChoice ambiguousTimeChoice)
    {
        if (!TimeZoneLabels.TryGetIanaZone(timeZoneId, out var normalizedId, out var timeZone))
        {
            throw new DomainException("The recurring series time-zone snapshot is unavailable.");
        }

        var local = DateTime.SpecifyKind(localDate.ToDateTime(localStartTime), DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            return new RecurringLocalStartResolution(localDate, localStartTime, true, null, null, []);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            var candidates = timeZone
                .GetAmbiguousTimeOffsets(local)
                .Select(offset => new RecurringLocalStartCandidate(
                    new DateTimeOffset(local, offset).ToUniversalTime(),
                    offset,
                    string.Empty))
                .OrderBy(candidate => candidate.UtcInstant)
                .Select(candidate => candidate with
                {
                    ZoneLabel = $"{TimeZoneLabels.StableLabel(normalizedId, timeZone, candidate.UtcInstant)} " +
                        $"({TimeZoneLabels.OffsetText(candidate.UtcOffset)})"
                })
                .ToArray();
            var selected = candidates[ambiguousTimeChoice == AmbiguousTimeChoice.FirstOccurrence ? 0 : 1];
            return new RecurringLocalStartResolution(
                localDate,
                localStartTime,
                false,
                selected.UtcInstant,
                selected.UtcOffset,
                candidates);
        }

        var offset = timeZone.GetUtcOffset(local);
        var instant = new DateTimeOffset(local, offset).ToUniversalTime();
        return new RecurringLocalStartResolution(localDate, localStartTime, false, instant, offset, []);
    }
}
