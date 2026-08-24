namespace VolunteerCoordinator.Application.Models;

public sealed record VolunteerDto(Guid Id, string Name, string Email, string? Phone);
