namespace VolunteerCoordinator.Application.Models;

public sealed record NotificationIntentDto(
    Guid Id,
    Guid VolunteerId,
    string VolunteerName,
    CommitmentDto? Commitment,
    string Kind,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    int AttemptCount,
    string? FailureCategory,
    IReadOnlyList<NotificationAttemptDto> Attempts,
    Guid? AccessAssignmentId = null);
