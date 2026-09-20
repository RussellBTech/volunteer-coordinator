namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorRequestDto(
    Guid RequestId,
    string VolunteerName,
    string VolunteerEmail,
    CommitmentDto Commitment,
    string Status,
    DateTimeOffset RequestedAtUtc,
    bool CanApprove,
    string SlotState)
{
    public string ShiftTitle => Commitment.ShiftTitle;

    public string SlotLabel => Commitment.SlotLabel;

    public DateTimeOffset StartsAtUtc => Commitment.StartsAtUtc;

    public DateTimeOffset EndsAtUtc => Commitment.EndsAtUtc;
}
