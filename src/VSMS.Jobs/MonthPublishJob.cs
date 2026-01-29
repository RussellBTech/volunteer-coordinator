using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Core.Interfaces;
using VSMS.Infrastructure.Data;

namespace VSMS.Jobs;

public class MonthPublishJob
{
    private readonly VsmsDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<MonthPublishJob> _logger;

    public MonthPublishJob(
        VsmsDbContext dbContext,
        IEmailService emailService,
        ILogger<MonthPublishJob> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// Sends monthly assignment emails to all volunteers with shifts in the specified month.
    /// Called when a month is published.
    /// </summary>
    public async Task SendMonthlyAssignmentEmails(int year, int month)
    {
        _logger.LogInformation("Starting monthly assignment emails for {Year}-{Month}", year, month);

        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        // Get all shifts with volunteers for this month
        var shifts = await _dbContext.Shifts
            .Include(s => s.Volunteer)
            .Include(s => s.TimeSlot)
            .Where(s => s.Date >= firstDay
                        && s.Date <= lastDay
                        && s.VolunteerId != null
                        && s.Status != ShiftStatus.Open)
            .ToListAsync();

        // Group by volunteer
        var shiftsByVolunteer = shifts
            .GroupBy(s => s.VolunteerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var successCount = 0;
        var errorCount = 0;

        foreach (var (volunteerId, volunteerShifts) in shiftsByVolunteer)
        {
            var volunteer = volunteerShifts.First().Volunteer!;

            try
            {
                await _emailService.SendMonthlyAssignmentEmailAsync(volunteer, volunteerShifts);
                successCount++;

                _logger.LogInformation("Sent monthly assignment email to {Email} for {Count} shifts",
                    volunteer.Email, volunteerShifts.Count);
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Failed to send monthly assignment email to {Email}", volunteer.Email);
            }
        }

        _logger.LogInformation(
            "Completed monthly assignment emails for {Year}-{Month}. Success: {Success}, Errors: {Errors}",
            year, month, successCount, errorCount);
    }
}
