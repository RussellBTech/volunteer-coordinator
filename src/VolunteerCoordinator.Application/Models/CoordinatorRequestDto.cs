namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorRequestDto(
    Guid RequestId,
    string VolunteerName,
    string VolunteerEmail,
    string ShiftTitle,
    string SlotLabel,
    DateTimeOffset StartsAtUtc,
    string Status,
    DateTimeOffset RequestedAtUtc,
    bool CanApprove,
    string SlotState);
