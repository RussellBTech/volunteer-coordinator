namespace VolunteerCoordinator.Application.Models;

public sealed record ConsequenceSlotDto(
    string SlotLabel,
    string State,
    string? VolunteerName,
    CommitmentDto Commitment);
