namespace VolunteerCoordinator.Application.Models;

public sealed record VolunteerSearchResultDto(
    Guid VolunteerId,
    string Name,
    string Email);
