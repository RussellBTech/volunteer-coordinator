namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorWorkSummaryDto(
    int TotalCount,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<CoordinatorWorkItemDto> Examples)
{
    public int Count(string category) => Counts.TryGetValue(category, out var count) ? count : 0;
}
