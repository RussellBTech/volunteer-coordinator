namespace VolunteerCoordinator.Application.Models;

public sealed record CoverageDto(
    Guid SlotId,
    Guid ShiftId,
    Guid? AssignmentId,
    string ShiftTitle,
    string SlotLabel,
    DateTimeOffset StartsAtUtc,
    string State,
    string? VolunteerName,
    string? VolunteerEmail);
