using VSMS.Core.Enums;

namespace VSMS.Core.Entities;

public class ShiftRequest
{
    public int Id { get; set; }
    public int ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;
    public int VolunteerId { get; set; }
    public Volunteer Volunteer { get; set; } = null!;
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByAdminId { get; set; }
    public AdminUser? ResolvedByAdmin { get; set; }
}
