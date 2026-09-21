namespace VolunteerCoordinator.Application.Models;

public sealed record AccessStateDto(
    string State,
    DateTimeOffset? LastMessageAtUtc = null,
    string? Guidance = null);
