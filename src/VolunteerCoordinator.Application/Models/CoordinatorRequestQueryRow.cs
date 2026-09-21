namespace VolunteerCoordinator.Application.Models;

public sealed class CoordinatorRequestQueryRow
{
    public Guid RequestId { get; set; }
    public Guid VolunteerId { get; set; }
    public string VolunteerName { get; set; } = string.Empty;
    public string VolunteerEmail { get; set; } = string.Empty;
    public Guid ShiftId { get; set; }
    public Guid SlotId { get; set; }
    public string ShiftTitle { get; set; } = string.Empty;
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public string? Location { get; set; }
    public string? VolunteerInstructions { get; set; }
    public int SlotKind { get; set; }
    public int SlotPosition { get; set; }
    public int SignupPolicy { get; set; }
    public int RequestStatus { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public bool CanApprove { get; set; }
    public string SlotState { get; set; } = string.Empty;
}
