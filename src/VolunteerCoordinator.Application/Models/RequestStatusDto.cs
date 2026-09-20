namespace VolunteerCoordinator.Application.Models;

public sealed record RequestStatusDto(
    Guid RequestId,
    string VolunteerName,
    CommitmentDto Commitment,
    string RequestStatus,
    string? AssignmentStatus)
{
    public string ShiftTitle => Commitment.ShiftTitle;

    public string SlotLabel => Commitment.SlotLabel;

    public DateTimeOffset StartsAtUtc => Commitment.StartsAtUtc;

    public DateTimeOffset EndsAtUtc => Commitment.EndsAtUtc;
}
