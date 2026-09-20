namespace VolunteerCoordinator.Application.Models;

public sealed record CommitmentDto(
    Guid ShiftId,
    Guid? SlotId,
    string ShiftTitle,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string GroupTimeZoneId,
    string? Location,
    string SlotLabel,
    string? VolunteerInstructions);
