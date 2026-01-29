using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Core.Interfaces;
using VSMS.Infrastructure.Data;

namespace VSMS.Jobs;

public class ShiftReminderJobs
{
    private readonly VsmsDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<ShiftReminderJobs> _logger;

    public ShiftReminderJobs(
        VsmsDbContext dbContext,
        IEmailService emailService,
        ILogger<ShiftReminderJobs> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// Sends reminder emails to volunteers with unconfirmed shifts 7+ days after month publication.
    /// Runs daily at 9am.
    /// </summary>
    public async Task SendSevenDayReminders()
    {
        _logger.LogInformation("Starting 7-day reminder job");

        var cutoffDate = DateOnly.FromDateTime(DateTime.Today);

        // Find shifts that:
        // - Were published at least 7 days ago
        // - Are still in Assigned status (not confirmed)
        // - Haven't had a 7-day reminder sent
        // - Are in the future
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

        var shiftsNeedingReminder = await _dbContext.Shifts
            .Include(s => s.Volunteer)
            .Include(s => s.TimeSlot)
            .Where(s => s.Status == ShiftStatus.Assigned
                        && s.MonthPublishedAt != null
                        && s.MonthPublishedAt <= sevenDaysAgo
                        && !s.ReminderSentAt7Days
                        && s.Date >= cutoffDate
                        && s.VolunteerId != null)
            .ToListAsync();

        // Group by volunteer
        var shiftsByVolunteer = shiftsNeedingReminder
            .GroupBy(s => s.VolunteerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (volunteerId, shifts) in shiftsByVolunteer)
        {
            var volunteer = shifts.First().Volunteer!;

            try
            {
                await _emailService.SendReminderEmailAsync(volunteer, shifts);

                // Mark reminders as sent
                foreach (var shift in shifts)
                {
                    shift.ReminderSentAt7Days = true;
                }
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Sent 7-day reminder to {Email} for {Count} shifts",
                    volunteer.Email, shifts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send 7-day reminder to {Email}", volunteer.Email);
            }
        }

        _logger.LogInformation("Completed 7-day reminder job. Sent {Count} reminders.", shiftsByVolunteer.Count);
    }

    /// <summary>
    /// Sends 24-hour reminder emails to volunteers with confirmed shifts.
    /// Runs hourly.
    /// </summary>
    public async Task Send24HourReminders()
    {
        _logger.LogInformation("Starting 24-hour reminder job");

        var now = DateTime.UtcNow;
        var tomorrow = DateOnly.FromDateTime(now.AddHours(24));
        var tomorrowStart = now.AddHours(23);
        var tomorrowEnd = now.AddHours(25);

        // Find confirmed shifts starting in ~24 hours that haven't had a reminder
        var shiftsNeedingReminder = await _dbContext.Shifts
            .Include(s => s.Volunteer)
            .Include(s => s.TimeSlot)
            .Where(s => s.Status == ShiftStatus.Confirmed
                        && s.Date == tomorrow
                        && !s.ReminderSentAt24Hours
                        && s.VolunteerId != null)
            .ToListAsync();

        // Filter by time window (shift starts in 23-25 hours)
        shiftsNeedingReminder = shiftsNeedingReminder
            .Where(s =>
            {
                var shiftStart = s.Date.ToDateTime(s.TimeSlot.StartTime);
                return shiftStart >= tomorrowStart && shiftStart <= tomorrowEnd;
            })
            .ToList();

        foreach (var shift in shiftsNeedingReminder)
        {
            try
            {
                await _emailService.Send24HourReminderAsync(shift.Volunteer!, shift);
                shift.ReminderSentAt24Hours = true;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Sent 24-hour reminder to {Email} for shift on {Date}",
                    shift.Volunteer!.Email, shift.Date);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send 24-hour reminder to {Email}", shift.Volunteer!.Email);
            }
        }

        _logger.LogInformation("Completed 24-hour reminder job. Sent {Count} reminders.", shiftsNeedingReminder.Count);
    }

    /// <summary>
    /// Automatically reopens shifts that are unconfirmed and starting within 24 hours.
    /// Runs hourly.
    /// </summary>
    public async Task AutoReopenUnconfirmedShifts()
    {
        _logger.LogInformation("Starting auto-reopen job");

        var now = DateTime.UtcNow;
        var cutoffTime = now.AddHours(24);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tomorrow = today.AddDays(1);

        // Find assigned (not confirmed) shifts starting within 24 hours
        var shiftsToReopen = await _dbContext.Shifts
            .Include(s => s.Volunteer)
            .Include(s => s.TimeSlot)
            .Where(s => s.Status == ShiftStatus.Assigned
                        && (s.Date == today || s.Date == tomorrow)
                        && s.VolunteerId != null)
            .ToListAsync();

        // Filter to only shifts starting within 24 hours
        shiftsToReopen = shiftsToReopen
            .Where(s =>
            {
                var shiftStart = s.Date.ToDateTime(s.TimeSlot.StartTime);
                var shiftStartUtc = DateTime.SpecifyKind(shiftStart, DateTimeKind.Utc);
                return shiftStartUtc <= cutoffTime && shiftStartUtc > now;
            })
            .ToList();

        foreach (var shift in shiftsToReopen)
        {
            var previousVolunteer = shift.Volunteer;

            // Reopen the shift
            shift.Status = ShiftStatus.Open;
            shift.VolunteerId = null;
            shift.AssignedAt = null;
            shift.ConfirmedAt = null;

            // Log the action
            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                ShiftId = shift.Id,
                VolunteerId = previousVolunteer?.Id,
                Action = "Auto-Reopened",
                Details = $"Shift auto-reopened due to no confirmation from {previousVolunteer?.Name}"
            });

            await _dbContext.SaveChangesAsync();

            _logger.LogWarning("Auto-reopened shift {ShiftId} on {Date} - was assigned to {Volunteer}",
                shift.Id, shift.Date, previousVolunteer?.Name);

            // Notify admin
            try
            {
                await _emailService.SendShiftReopenedToAdminAsync(shift);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reopen notification for shift {ShiftId}", shift.Id);
            }

            // Escalate to backup volunteers
            try
            {
                var backupVolunteers = await _dbContext.Volunteers
                    .Where(v => v.IsBackup && v.IsActive)
                    .ToListAsync();

                if (backupVolunteers.Any())
                {
                    await _emailService.SendEscalationToBackupsAsync(shift, backupVolunteers);
                    _logger.LogInformation("Sent escalation to {Count} backup volunteers for shift {ShiftId}",
                        backupVolunteers.Count, shift.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send escalation to backup volunteers for shift {ShiftId}", shift.Id);
            }
        }

        _logger.LogInformation("Completed auto-reopen job. Reopened {Count} shifts.", shiftsToReopen.Count);
    }
}
