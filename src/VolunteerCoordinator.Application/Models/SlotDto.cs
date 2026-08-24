namespace VolunteerCoordinator.Application.Models;

public sealed record SlotDto(Guid Id, string Kind, int Position, string Status);
