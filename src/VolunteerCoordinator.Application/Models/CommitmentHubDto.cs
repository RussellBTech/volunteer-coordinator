namespace VolunteerCoordinator.Application.Models;

public sealed record CommitmentHubDto(
    Guid CapabilityId,
    Guid RequestId,
    string VolunteerName,
    CommitmentDto Commitment,
    string RequestStatus,
    string? AssignmentStatus,
    IReadOnlyList<string> OfferedActions,
    string StatusMessage)
{
    public bool CanConfirm => OfferedActions.Contains("Confirm", StringComparer.Ordinal);

    public bool CanDecline => OfferedActions.Contains("Decline", StringComparer.Ordinal);

    public bool CanCancel => OfferedActions.Contains("Cancel", StringComparer.Ordinal);
}
