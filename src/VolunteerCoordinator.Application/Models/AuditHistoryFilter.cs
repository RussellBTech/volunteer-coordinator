namespace VolunteerCoordinator.Application.Models;

public sealed record AuditHistoryFilter(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ThroughExclusiveUtc = null,
    string? Actor = null,
    Guid? ShiftId = null,
    Guid? VolunteerId = null,
    string? Category = null)
{
    public string? NormalizedActor => string.IsNullOrWhiteSpace(Actor) ? null : Actor.Trim().ToUpperInvariant();

    public string? NormalizedCategory => string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();
}
