namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorHomeExample(
    Guid ShiftId,
    Guid? SlotId,
    string ShiftTitle,
    string SlotLabel,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string? VolunteerName,
    DateTimeOffset? OccurredAtUtc,
    string? MessageKind,
    string? VolunteerEmail = null,
    string? VolunteerPhone = null);
