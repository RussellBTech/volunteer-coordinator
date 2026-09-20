namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorActionPreviewDto(
    string ActionKey,
    Guid TargetId,
    Guid ShiftId,
    Guid? SlotId,
    uint ExpectedShiftVersion,
    Guid? ExpectedAssignmentId,
    Guid? ExpectedVolunteerId,
    string? ExpectedAssignmentState,
    CommitmentDto Commitment,
    IReadOnlyList<ConsequenceSlotDto> Slots,
    IReadOnlyList<ConsequencePersonDto> AffectedPeople,
    PreviewVolunteerDto? CurrentVolunteer,
    PreviewVolunteerDto? ReplacementVolunteer,
    IReadOnlyList<string> Consequences,
    bool ReplacementReusesExistingVolunteer = false,
    uint ExpectedSettingsVersion = 0)
{
    public bool IsPublish => string.Equals(ActionKey, "publish", StringComparison.Ordinal);

    public bool IsDeactivate => string.Equals(ActionKey, "deactivate", StringComparison.Ordinal);

    public bool IsCancel => string.Equals(ActionKey, "cancel", StringComparison.Ordinal);

    public bool IsReplacement => string.Equals(ActionKey, "replace", StringComparison.Ordinal);

    public string ConfirmLabel => ActionKey switch
    {
        "publish" => "Publish commitments",
        "deactivate" => "Deactivate and resolve",
        "cancel" => "Cancel assignment",
        "replace" => "Replace volunteer",
        "assign" => "Assign volunteer",
        _ => "Confirm change"
    };
    public string ExpectedAffectedSet { get; init; } = string.Empty;
    public Guid? ExpectedSelectedVolunteerId { get; init; }

    public string? ExpectedSelectedVolunteerNormalizedEmail { get; init; }
}
