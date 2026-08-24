namespace VolunteerCoordinator.Application.Models;

public sealed record ActionInspectionDto(
    string VolunteerName,
    string ShiftTitle,
    string SlotLabel,
    DateTimeOffset StartsAtUtc,
    string Action,
    string AssignmentStatus,
    bool CanApply,
    string Message);
