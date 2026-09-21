namespace VolunteerCoordinator.Application.Models;

public sealed record AuditHistoryCursor(
    DateTimeOffset OccurredAtUtc,
    Guid AuditId,
    bool Forward = true);
