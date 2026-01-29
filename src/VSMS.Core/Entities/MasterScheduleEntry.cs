using VSMS.Core.Enums;

namespace VSMS.Core.Entities;

public class MasterScheduleEntry
{
    public int Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public int TimeSlotId { get; set; }
    public TimeSlot TimeSlot { get; set; } = null!;
    public ShiftRole Role { get; set; }
    public int? DefaultVolunteerId { get; set; }
    public Volunteer? DefaultVolunteer { get; set; }
    public bool IsClosed { get; set; }
}
