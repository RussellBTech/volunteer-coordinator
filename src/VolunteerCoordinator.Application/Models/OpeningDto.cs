namespace VolunteerCoordinator.Application.Models;

public sealed record OpeningDto(CommitmentDto Commitment, string Status)
{
    public Guid SlotId => Commitment.SlotId ?? Guid.Empty;

    public Guid ShiftId => Commitment.ShiftId;

    public string ShiftTitle => Commitment.ShiftTitle;

    public string? Location => Commitment.Location;

    public DateTimeOffset StartsAtUtc => Commitment.StartsAtUtc;

    public DateTimeOffset EndsAtUtc => Commitment.EndsAtUtc;

    public string SlotLabel => Commitment.SlotLabel;

    public string? VolunteerInstructions => Commitment.VolunteerInstructions;
}
