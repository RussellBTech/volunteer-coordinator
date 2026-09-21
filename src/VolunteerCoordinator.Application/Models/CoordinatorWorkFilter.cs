namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorWorkFilter(
    string? Category = null,
    int? SeverityRank = null)
{
    public string? NormalizedCategory => string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();
}
