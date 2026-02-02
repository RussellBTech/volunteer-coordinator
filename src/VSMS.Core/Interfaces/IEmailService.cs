using VSMS.Core.Entities;

namespace VSMS.Core.Interfaces;

public interface IEmailService
{
    Task SendMonthlyAssignmentEmailAsync(Volunteer volunteer, List<Shift> shifts);
    Task SendReminderEmailAsync(Volunteer volunteer, List<Shift> unconfirmedShifts);
    Task Send24HourReminderAsync(Volunteer volunteer, Shift shift);
    Task SendShiftRequestReceivedAsync(Volunteer volunteer, Shift shift);
    Task SendShiftApprovedAsync(Volunteer volunteer, Shift shift);
    Task SendShiftRejectedAsync(Volunteer volunteer, Shift shift);
    Task SendShiftReopenedToAdminAsync(Shift shift);
    Task SendEscalationToBackupsAsync(Shift shift, List<Volunteer> backups);
    Task SendEscalationToAllAsync(Shift shift, List<Volunteer> volunteers);
}
