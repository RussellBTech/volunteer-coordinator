namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorWorkPageDto(
    IReadOnlyList<CoordinatorWorkItemDto> Items,
    bool HasNextPage,
    bool HasPreviousPage,
    string? PositionMessage = null);
