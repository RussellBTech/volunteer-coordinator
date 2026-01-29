using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Core.Interfaces;
using VSMS.Infrastructure.Data;

namespace VSMS.Infrastructure.Services;

public class GoogleCalendarService : ICalendarService
{
    private readonly VsmsDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleCalendarService> _logger;
    private readonly string? _calendarId;
    private CalendarService? _calendarService;

    public GoogleCalendarService(
        VsmsDbContext dbContext,
        IConfiguration configuration,
        ILogger<GoogleCalendarService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
        _calendarId = configuration["Google:CalendarId"];
    }

    private Task<CalendarService?> GetCalendarServiceAsync()
    {
        if (_calendarService != null)
            return Task.FromResult<CalendarService?>(_calendarService);

        var keyPath = _configuration["Google:ServiceAccountKeyPath"];
        if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath))
        {
            _logger.LogWarning("Google service account key not configured or not found");
            return null;
        }

        try
        {
            using var stream = new FileStream(keyPath, FileMode.Open, FileAccess.Read);
            var credential = GoogleCredential.FromStream(stream)
                .CreateScoped(CalendarService.Scope.Calendar);

            _calendarService = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Volunteer Shift Management System"
            });

            return Task.FromResult<CalendarService?>(_calendarService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Google Calendar service");
            return Task.FromResult<CalendarService?>(null);
        }
    }

    public async Task<string?> CreateEventAsync(Shift shift)
    {
        var service = await GetCalendarServiceAsync();
        if (service == null || string.IsNullOrEmpty(_calendarId))
        {
            _logger.LogWarning("Calendar service not available, skipping event creation");
            return null;
        }

        try
        {
            var calendarEvent = MapShiftToEvent(shift);
            var created = await service.Events.Insert(calendarEvent, _calendarId).ExecuteAsync();

            _logger.LogInformation("Created calendar event {EventId} for shift {ShiftId}", created.Id, shift.Id);
            return created.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create calendar event for shift {ShiftId}", shift.Id);
            return null;
        }
    }

    public async Task UpdateEventAsync(Shift shift)
    {
        if (string.IsNullOrEmpty(shift.GoogleCalendarEventId))
        {
            // No existing event, create one
            var eventId = await CreateEventAsync(shift);
            if (eventId != null)
            {
                shift.GoogleCalendarEventId = eventId;
                await _dbContext.SaveChangesAsync();
            }
            return;
        }

        var service = await GetCalendarServiceAsync();
        if (service == null || string.IsNullOrEmpty(_calendarId))
            return;

        try
        {
            var calendarEvent = MapShiftToEvent(shift);
            await service.Events.Update(calendarEvent, _calendarId, shift.GoogleCalendarEventId).ExecuteAsync();

            _logger.LogInformation("Updated calendar event {EventId} for shift {ShiftId}",
                shift.GoogleCalendarEventId, shift.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update calendar event {EventId} for shift {ShiftId}",
                shift.GoogleCalendarEventId, shift.Id);
        }
    }

    public async Task DeleteEventAsync(string eventId)
    {
        var service = await GetCalendarServiceAsync();
        if (service == null || string.IsNullOrEmpty(_calendarId))
            return;

        try
        {
            await service.Events.Delete(_calendarId, eventId).ExecuteAsync();
            _logger.LogInformation("Deleted calendar event {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete calendar event {EventId}", eventId);
        }
    }

    public async Task SyncShiftAsync(Shift shift)
    {
        if (shift.MonthPublishedAt == null)
        {
            // Don't sync unpublished shifts
            return;
        }

        await UpdateEventAsync(shift);
    }

    private Event MapShiftToEvent(Shift shift)
    {
        var title = shift.Status == ShiftStatus.Open
            ? $"OPEN - {shift.Role}"
            : $"{shift.Volunteer?.Name ?? "Assigned"} - {shift.Role}";

        var colorId = shift.Status switch
        {
            ShiftStatus.Open => "11",      // Red
            ShiftStatus.Assigned => "5",   // Yellow
            ShiftStatus.Confirmed => "10", // Green
            _ => "8"                        // Gray
        };

        var startDateTime = shift.Date.ToDateTime(shift.TimeSlot.StartTime);
        var endDateTime = startDateTime.AddMinutes(shift.TimeSlot.DurationMinutes);

        var description = shift.Status switch
        {
            ShiftStatus.Open => "This shift is open and needs coverage.",
            ShiftStatus.Assigned => $"Assigned to {shift.Volunteer?.Name ?? "Unknown"} - awaiting confirmation.",
            ShiftStatus.Confirmed => $"Confirmed - {shift.Volunteer?.Name ?? "Unknown"} is scheduled.",
            _ => ""
        };

        if (shift.Role == ShiftRole.Phone)
        {
            description += "\n\nPhone shift - calls will be forwarded.";
        }
        else
        {
            description += "\n\nIn-person shift at the Intergroup office.";
        }

        return new Event
        {
            Summary = title,
            Description = description,
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(startDateTime, TimeSpan.FromHours(-6)),
                TimeZone = "America/Chicago" // Adjust to your timezone
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = new DateTimeOffset(endDateTime, TimeSpan.FromHours(-6)),
                TimeZone = "America/Chicago"
            },
            ColorId = colorId
        };
    }
}
