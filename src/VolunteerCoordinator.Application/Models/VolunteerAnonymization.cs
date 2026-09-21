namespace VolunteerCoordinator.Application.Models;

public enum VolunteerAnonymizationReason
{
    RetentionExpired,
    CoordinatorRequest
}

public enum VolunteerAnonymizationOutcome
{
    Anonymized,
    AlreadyAnonymized,
    NotFound,
    Blocked,
    Failed
}

public enum VolunteerAnonymizationBlocker
{
    None,
    TooRecent,
    PendingRequests,
    ActiveAssignments,
    ActiveRecurringCommitments,
    FutureCommitments
}

public sealed record VolunteerAnonymizationResult(
    VolunteerAnonymizationOutcome Outcome,
    VolunteerAnonymizationBlocker Blocker,
    int BlockingCount,
    DateTimeOffset? AnchorUtc,
    int NotificationDestinationsRedacted)
{
    public bool IsSuccessful => Outcome is VolunteerAnonymizationOutcome.Anonymized or VolunteerAnonymizationOutcome.AlreadyAnonymized;

    public int RequestCount { get; init; }

    public int AssignmentCount { get; init; }
    public int CapabilitiesInvalidated { get; init; }

    public int RecoveryTokensInvalidated { get; init; }
    public int ActionTokensInvalidated { get; init; }
}

public sealed record RetentionSweepResult(
    Guid VolunteerId,
    VolunteerAnonymizationResult? Result,
    bool Failed,
    string? FailureReason = null);
