namespace VolunteerCoordinator.Web.Security;

public sealed record CoordinatorAssignmentReviewState(
    Guid SlotId,
    string ActionKey,
    string Mode,
    Guid? KnownVolunteerId,
    string? VolunteerName,
    string? VolunteerEmail,
    string? VolunteerPhone,
    Guid? ExpectedAssignmentId,
    Guid? ExpectedVolunteerId,
    string? ExpectedAssignmentState,
    uint ExpectedShiftVersion,
    uint? ExpectedSettingsVersion,
    string ExpectedAffectedSet,
    Guid? ExpectedSelectedVolunteerId,
    string? ExpectedSelectedVolunteerNormalizedEmail,
    string SearchTerm = "");
