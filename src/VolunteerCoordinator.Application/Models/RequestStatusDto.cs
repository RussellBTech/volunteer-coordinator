namespace VolunteerCoordinator.Application.Models;

public sealed record RequestStatusDto(
    Guid RequestId,
    string VolunteerName,
    string ShiftTitle,
    string SlotLabel,
    DateTimeOffset StartsAtUtc,
    string RequestStatus,
    string? AssignmentStatus);
