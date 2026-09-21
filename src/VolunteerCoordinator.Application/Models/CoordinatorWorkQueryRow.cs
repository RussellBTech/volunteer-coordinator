namespace VolunteerCoordinator.Application.Models;

public sealed class CoordinatorWorkQueryRow
{
    public Guid StableId { get; set; }
    public string Category { get; set; } = string.Empty;
    public int CategoryRank { get; set; }
    public int SeverityRank { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public DateTimeOffset? StartsAtUtc { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }
    public Guid? ShiftId { get; set; }
    public Guid? SlotId { get; set; }
    public Guid? VolunteerId { get; set; }
    public Guid? SeriesId { get; set; }
    public Guid? CommitmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string? PersonName { get; set; }
    public string State { get; set; } = string.Empty;
    public string RouteKind { get; set; } = string.Empty;
    public Guid RouteId { get; set; }
    public int CategoryCount { get; set; }
    public int TotalCount { get; set; }
    public long RowNumber { get; set; }
}
