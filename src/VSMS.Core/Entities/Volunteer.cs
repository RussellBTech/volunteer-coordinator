namespace VSMS.Core.Entities;

public class Volunteer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public bool IsBackup { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Shift> Shifts { get; set; } = new List<Shift>();
    public ICollection<MasterScheduleEntry> DefaultScheduleEntries { get; set; } = new List<MasterScheduleEntry>();
    public ICollection<ActionToken> ActionTokens { get; set; } = new List<ActionToken>();
    public ICollection<ShiftRequest> ShiftRequests { get; set; } = new List<ShiftRequest>();
}
