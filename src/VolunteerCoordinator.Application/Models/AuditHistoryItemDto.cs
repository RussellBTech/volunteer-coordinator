namespace VolunteerCoordinator.Application.Models;

public sealed record AuditHistoryItemDto(
    Guid AuditId,
    DateTimeOffset OccurredAtUtc,
    string ActorDisplay,
    string Category,
    string Summary,
    string? VolunteerReference = null);
