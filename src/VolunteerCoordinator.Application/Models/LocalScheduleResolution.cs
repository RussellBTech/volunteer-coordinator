namespace VolunteerCoordinator.Application.Models;

public sealed record LocalScheduleResolution(
    DateTime StartsAtLocal,
    DateTime EndsAtLocal,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    IReadOnlyList<LocalTimeCandidate> StartCandidates,
    IReadOnlyList<LocalTimeCandidate> EndCandidates,
    IReadOnlyList<string> Errors)
{
    public bool IsComplete => StartsAtUtc.HasValue && EndsAtUtc.HasValue && Errors.Count == 0;

    public bool RequiresStartOffset => StartCandidates.Count > 0 && !StartsAtUtc.HasValue;

    public bool RequiresEndOffset => EndCandidates.Count > 0 && !EndsAtUtc.HasValue;
}
