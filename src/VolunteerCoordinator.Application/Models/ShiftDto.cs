namespace VolunteerCoordinator.Application.Models;

public sealed record ShiftDto(
    CommitmentDto Commitment,
    string? InternalCoordinatorNotes,
    bool IsActive,
    bool IsPublished,
    uint Version,
    IReadOnlyList<SlotDto> Slots)
{
    public Guid Id => Commitment.ShiftId;

    public string Title => Commitment.ShiftTitle;

    public string? Location => Commitment.Location;

    public string? Notes => InternalCoordinatorNotes;

    public DateTimeOffset StartsAtUtc => Commitment.StartsAtUtc;

    public DateTimeOffset EndsAtUtc => Commitment.EndsAtUtc;
}
