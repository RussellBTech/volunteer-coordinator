using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Models;

public sealed record CommitmentDto(
    Guid ShiftId,
    Guid? SlotId,
    string ShiftTitle,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string GroupTimeZoneId,
    string? Location,
    string SlotLabel,
    string? VolunteerInstructions)
{
    public SignupPolicy SignupPolicy { get; init; } = SignupPolicy.ApprovalRequired;

    public string SignupPolicyLabel => SignupPolicy == SignupPolicy.DirectClaim
        ? "Direct claim"
        : "Approval required";

    public string SignupPolicyConsequence => SignupPolicy == SignupPolicy.DirectClaim
        ? "The first eligible volunteer is confirmed immediately."
        : "A coordinator reviews each request before the commitment is assigned.";
}
