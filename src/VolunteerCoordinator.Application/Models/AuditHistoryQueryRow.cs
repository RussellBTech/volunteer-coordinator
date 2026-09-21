namespace VolunteerCoordinator.Application.Models;

public sealed class AuditHistoryQueryRow
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Guid? ShiftId { get; set; }
    public Guid? VolunteerId { get; set; }
    public string? ShiftTitle { get; set; }
    public DateTimeOffset? ShiftStartsAtUtc { get; set; }
    public string? VolunteerName { get; set; }
    public bool VolunteerIsAnonymized { get; set; }
}
