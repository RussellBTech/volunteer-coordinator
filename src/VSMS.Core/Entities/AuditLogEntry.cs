namespace VSMS.Core.Entities;

public class AuditLogEntry
{
    public int Id { get; set; }
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public int? VolunteerId { get; set; }
    public Volunteer? Volunteer { get; set; }
    public int? AdminUserId { get; set; }
    public AdminUser? AdminUser { get; set; }
    public required string Action { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
