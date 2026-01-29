namespace VSMS.Core.Entities;

public class TimeSlot
{
    public int Id { get; set; }
    public required string Label { get; set; }
    public TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<Shift> Shifts { get; set; } = new List<Shift>();
    public ICollection<MasterScheduleEntry> MasterScheduleEntries { get; set; } = new List<MasterScheduleEntry>();
}
