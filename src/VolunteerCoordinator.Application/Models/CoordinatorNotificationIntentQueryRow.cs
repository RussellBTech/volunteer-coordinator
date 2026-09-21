namespace VolunteerCoordinator.Application.Models;

public sealed class CoordinatorNotificationIntentQueryRow
{
    public Guid Id { get; set; }
    public Guid VolunteerId { get; set; }
    public string VolunteerName { get; set; } = string.Empty;
    public Guid? ShiftSlotId { get; set; }
    public Guid? ShiftId { get; set; }
    public string? ShiftTitle { get; set; }
    public DateTimeOffset? StartsAtUtc { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }
    public string? Location { get; set; }
    public string? VolunteerInstructions { get; set; }
    public int SlotKind { get; set; }
    public int SignupPolicy { get; set; }
    public string GroupTimeZoneId { get; set; } = "Etc/UTC";
    public int SlotPosition { get; set; }
    public int State { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? FailureCategory { get; set; }
    public Guid? AccessAssignmentId { get; set; }
}
