namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorWorkCursor(
    int SeverityRank,
    DateTimeOffset DueAtUtc,
    int CategoryRank,
    Guid StableId,
    bool Forward = true);
