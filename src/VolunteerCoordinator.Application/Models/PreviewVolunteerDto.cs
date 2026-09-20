namespace VolunteerCoordinator.Application.Models;

public sealed record PreviewVolunteerDto(
    string Name,
    string? Email,
    string? Phone = null);
