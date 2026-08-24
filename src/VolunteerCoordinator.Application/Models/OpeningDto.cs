namespace VolunteerCoordinator.Application.Models;

public sealed record OpeningDto(
    Guid SlotId,
    Guid ShiftId,
    string ShiftTitle,
    string? Location,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string SlotLabel,
    string Status);
