using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Settings;

namespace VolunteerCoordinator.Application.Time;

public static class LocalScheduleResolver
{
    public const string InvalidTimeMessage =
        "This local time does not exist because the clocks move forward. Choose another time.";

    public static LocalScheduleResolution Resolve(
        GroupSettings settings,
        LocalScheduleInput input)
    {
        var startsAtLocal = input.StartsAtUnspecified;
        var endsAtLocal = input.EndsAtUnspecified;
        var errors = new List<string>();
        var normalizedId = settings.TimeZoneId.Trim();
        if (!TimeZoneLabels.TryGetIanaZone(settings.TimeZoneId, out _, out var timeZone))
        {
            errors.Add("The configured group time zone is unavailable. Contact a coordinator.");
            return new LocalScheduleResolution(
                startsAtLocal,
                endsAtLocal,
                null,
                null,
                [],
                [],
                errors);
        }
        var start = ResolveBoundary(
            startsAtLocal,
            input.StartsAtOffset,
            "start",
            normalizedId,
            timeZone,
            errors);
        var end = ResolveBoundary(
            endsAtLocal,
            input.EndsAtOffset,
            "end",
            normalizedId,
            timeZone,
            errors);

        if (start.Instant.HasValue && end.Instant.HasValue && end.Instant <= start.Instant)
        {
            errors.Add("The local end must be after the local start.");
        }

        return new LocalScheduleResolution(
            startsAtLocal,
            endsAtLocal,
            start.Instant,
            end.Instant,
            start.Candidates,
            end.Candidates,
            errors);
    }

    private static BoundaryResult ResolveBoundary(
        DateTime localTime,
        TimeSpan? selectedOffset,
        string boundaryName,
        string normalizedId,
        TimeZoneInfo timeZone,
        ICollection<string> errors)
    {
        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localTime))
        {
            errors.Add(InvalidTimeMessage);
            return new BoundaryResult(null, []);
        }

        if (timeZone.IsAmbiguousTime(localTime))
        {
            var candidates = timeZone
                .GetAmbiguousTimeOffsets(localTime)
                .Select(offset => new LocalTimeCandidate(
                    localTime,
                    offset,
                    new DateTimeOffset(localTime, offset).ToUniversalTime(),
                    string.Empty))
                .OrderBy(candidate => candidate.UtcInstant)
                .Select(candidate => candidate with
                {
                    ZoneLabel = TimeZoneLabels.LocalCandidateLabel(
                        new LocalScheduleCandidateParts(
                            candidate.LocalTime,
                            candidate.UtcOffset,
                            candidate.UtcInstant),
                        normalizedId,
                        timeZone)
                })
                .ToArray();
            if (!selectedOffset.HasValue)
            {
                return new BoundaryResult(null, candidates);
            }

            var selected = candidates.SingleOrDefault(x => x.UtcOffset == selectedOffset.Value);
            if (selected is null)
            {
                errors.Add($"The selected interpretation for the {boundaryName} time is no longer valid. Choose one of the displayed interpretations.");
                return new BoundaryResult(null, candidates);
            }

            return new BoundaryResult(selected.UtcInstant, candidates);
        }

        var derivedOffset = timeZone.GetUtcOffset(localTime);
        if (selectedOffset.HasValue && selectedOffset.Value != derivedOffset)
        {
            errors.Add($"The selected interpretation does not match the {boundaryName} time. Choose another interpretation.");
            return new BoundaryResult(null, []);
        }

        return new BoundaryResult(
            new DateTimeOffset(localTime, derivedOffset).ToUniversalTime(),
            []);
    }

    private sealed record BoundaryResult(
        DateTimeOffset? Instant,
        IReadOnlyList<LocalTimeCandidate> Candidates);
}
