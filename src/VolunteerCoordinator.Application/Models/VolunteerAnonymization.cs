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
    FutureCommitments
}

public sealed record VolunteerAnonymizationResult(
    VolunteerAnonymizationOutcome Outcome,
    VolunteerAnonymizationBlocker Blocker,
    int BlockingCount,
    DateTimeOffset? AnchorUtc,
    int StatusTokensInvalidated,
    int ActionTokensInvalidated,
    int NotificationDestinationsRedacted)
{
    public bool IsSuccessful => Outcome is VolunteerAnonymizationOutcome.Anonymized or VolunteerAnonymizationOutcome.AlreadyAnonymized;

    public int RequestCount { get; init; }

    public int AssignmentCount { get; init; }
}

public sealed record RetentionSweepResult(
    Guid VolunteerId,
    VolunteerAnonymizationResult? Result,
    bool Failed,
    string? FailureReason = null);
