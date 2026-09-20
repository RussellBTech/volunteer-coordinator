namespace VolunteerCoordinator.Application.Models;

public sealed record CoverageDto(
    Guid SlotId,
    Guid ShiftId,
    Guid? AssignmentId,
    CommitmentDto Commitment,
    string State,
    string? VolunteerName,
    string? VolunteerEmail)
{
    public string ShiftTitle => Commitment.ShiftTitle;

    public string SlotLabel => Commitment.SlotLabel;

    public DateTimeOffset StartsAtUtc => Commitment.StartsAtUtc;

    public DateTimeOffset EndsAtUtc => Commitment.EndsAtUtc;
}
