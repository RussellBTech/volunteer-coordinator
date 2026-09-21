namespace VolunteerCoordinator.Application.Models;

public sealed class CoordinatorCoverageQueryRow
{
    public Guid SlotId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid? AssignmentId { get; set; }
    public Guid? VolunteerId { get; set; }
    public string ShiftTitle { get; set; } = string.Empty;
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public string? Location { get; set; }
    public string? VolunteerInstructions { get; set; }
    public int SlotKind { get; set; }
    public int SlotPosition { get; set; }
    public int SignupPolicy { get; set; }
    public string State { get; set; } = string.Empty;
    public string? VolunteerName { get; set; }
    public string? VolunteerEmail { get; set; }
    public string? AccessState { get; set; }
    public DateTimeOffset? AccessLastMessageAtUtc { get; set; }
}
