namespace VolunteerCoordinator.Application.Models;

public sealed record SetupStepDto(
    int Number,
    string Title,
    string Description,
    string Status,
    bool IsComplete,
    bool IsCurrent,
    bool IsInformational,
    string? Url);
