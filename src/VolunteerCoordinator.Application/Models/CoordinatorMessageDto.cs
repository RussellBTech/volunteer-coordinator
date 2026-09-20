namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorMessageDto(
    string VolunteerName,
    CommitmentDto Commitment,
    DateTimeOffset OccurredAtUtc,
    string Purpose,
    string Guidance,
    string? VolunteerEmail = null,
    string? VolunteerPhone = null);
