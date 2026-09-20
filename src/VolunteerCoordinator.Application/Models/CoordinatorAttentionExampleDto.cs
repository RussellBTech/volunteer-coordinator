namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorAttentionExampleDto(
    CommitmentDto Commitment,
    string? PersonName,
    DateTimeOffset? OccurredAtUtc,
    string? MessagePurpose);
