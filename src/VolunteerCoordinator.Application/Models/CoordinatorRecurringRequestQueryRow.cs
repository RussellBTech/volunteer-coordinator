namespace VolunteerCoordinator.Application.Models;

public sealed class CoordinatorRecurringRequestQueryRow
{
    public Guid RequestId { get; set; }
    public Guid VolunteerId { get; set; }
    public string VolunteerName { get; set; } = string.Empty;
    public string VolunteerEmail { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int RoleKind { get; set; }
    public int RolePosition { get; set; }
    public int SourcePolicy { get; set; }
    public int Status { get; set; }
    public DateOnly EffectiveLocalDate { get; set; }
    public DateOnly EndLocalDate { get; set; }
    public int IncludedCount { get; set; }
    public int TotalCount { get; set; }
    public Guid? RecurringCommitmentId { get; set; }
}
