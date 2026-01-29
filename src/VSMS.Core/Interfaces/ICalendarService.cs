using VSMS.Core.Entities;

namespace VSMS.Core.Interfaces;

public interface ICalendarService
{
    Task<string?> CreateEventAsync(Shift shift);
    Task UpdateEventAsync(Shift shift);
    Task DeleteEventAsync(string eventId);
    Task SyncShiftAsync(Shift shift);
}
