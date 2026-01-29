using VSMS.Core.Enums;

namespace VSMS.Core.Entities;

public class Shift
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public int TimeSlotId { get; set; }
    public TimeSlot TimeSlot { get; set; } = null!;
    public ShiftRole Role { get; set; }
    public int? VolunteerId { get; set; }
    public Volunteer? Volunteer { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
    public DateTime? AssignedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? MonthPublishedAt { get; set; }
    public string? GoogleCalendarEventId { get; set; }
    public bool ReminderSentAt7Days { get; set; }
    public bool ReminderSentAt24Hours { get; set; }

    public ICollection<ActionToken> ActionTokens { get; set; } = new List<ActionToken>();
    public ICollection<ShiftRequest> ShiftRequests { get; set; } = new List<ShiftRequest>();

    public DateTime GetStartDateTime()
    {
        return Date.ToDateTime(TimeSlot.StartTime);
    }

    public DateTime GetEndDateTime()
    {
        return GetStartDateTime().AddMinutes(TimeSlot.DurationMinutes);
    }
}
