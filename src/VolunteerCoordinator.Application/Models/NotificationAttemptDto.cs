namespace VolunteerCoordinator.Application.Models;

public sealed record NotificationAttemptDto(
    int Ordinal,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? OutcomeCategory);
