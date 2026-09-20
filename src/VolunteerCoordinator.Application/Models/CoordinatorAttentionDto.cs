namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorAttentionDto(
    string Key,
    string Label,
    int Count,
    string Url,
    string ActionLabel,
    IReadOnlyList<CoordinatorAttentionExampleDto> Examples);
