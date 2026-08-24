namespace VolunteerCoordinator.Application.Models;

public sealed record AuditDto(
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Action,
    string EntityKind,
    Guid EntityId,
    string DetailJson);
