namespace VolunteerCoordinator.Application.Models;

public sealed record ActionInspectionDto(
    string VolunteerName,
    CommitmentDto Commitment,
    string Action,
    string AssignmentStatus,
    bool CanApply,
    string Message)
{
    public string ShiftTitle => Commitment.ShiftTitle;

    public string SlotLabel => Commitment.SlotLabel;

    public DateTimeOffset StartsAtUtc => Commitment.StartsAtUtc;

    public DateTimeOffset EndsAtUtc => Commitment.EndsAtUtc;
}
