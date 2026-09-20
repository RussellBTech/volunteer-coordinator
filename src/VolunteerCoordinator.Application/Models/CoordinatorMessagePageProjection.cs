namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorMessagePageProjection(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CoordinatorHomeExample> Items);
