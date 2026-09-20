namespace VolunteerCoordinator.Application.Models;

public sealed record ConsequencePersonDto(
    string Name,
    string SlotLabel,
    CommitmentDto Commitment,
    string Consequence)
{
    public Guid? AffectedRequestId { get; init; }

    public Guid? AffectedAssignmentId { get; init; }

    public Guid? AffectedVolunteerId { get; init; }
}
