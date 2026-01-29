using VSMS.Core.Enums;

namespace VSMS.Core.Entities;

public class ActionToken
{
    public int Id { get; set; }
    public required string Token { get; set; }
    public int ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;
    public int VolunteerId { get; set; }
    public Volunteer Volunteer { get; set; } = null!;
    public TokenAction Action { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsValid => UsedAt == null && ExpiresAt > DateTime.UtcNow;
}
