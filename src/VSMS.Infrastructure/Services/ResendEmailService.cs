using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resend;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Core.Interfaces;

namespace VSMS.Infrastructure.Services;

public class ResendEmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResendEmailService> _logger;
    private readonly string _fromAddress;
    private readonly string _fromName;

    public ResendEmailService(
        IResend resend,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<ResendEmailService> logger)
    {
        _resend = resend;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
        _fromAddress = configuration["Email:FromAddress"] ?? "noreply@example.com";
        _fromName = configuration["Email:FromName"] ?? "Volunteer Shifts";
    }

    public async Task SendMonthlyAssignmentEmailAsync(Volunteer volunteer, List<Shift> shifts)
    {
        if (!shifts.Any()) return;

        var monthYear = shifts.First().Date.ToString("MMMM yyyy");
        var subject = $"Your {monthYear} Volunteer Shifts";

        var body = new StringBuilder();
        body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
        body.AppendLine($"<p>Here are your volunteer shifts for {monthYear}:</p>");
        body.AppendLine("<table style='border-collapse: collapse; width: 100%;'>");
        body.AppendLine("<tr style='background-color: #f0f0f0;'>");
        body.AppendLine("<th style='padding: 10px; text-align: left;'>Date</th>");
        body.AppendLine("<th style='padding: 10px; text-align: left;'>Time</th>");
        body.AppendLine("<th style='padding: 10px; text-align: left;'>Role</th>");
        body.AppendLine("<th style='padding: 10px; text-align: left;'>Actions</th>");
        body.AppendLine("</tr>");

        foreach (var shift in shifts.OrderBy(s => s.Date).ThenBy(s => s.TimeSlot.SortOrder))
        {
            var confirmToken = await _tokenService.CreateTokenAsync(shift.Id, volunteer.Id, TokenAction.Confirm);
            var declineToken = await _tokenService.CreateTokenAsync(shift.Id, volunteer.Id, TokenAction.Decline);

            body.AppendLine("<tr>");
            body.AppendLine($"<td style='padding: 10px; border-bottom: 1px solid #ddd;'>{shift.Date:dddd, MMM d}</td>");
            body.AppendLine($"<td style='padding: 10px; border-bottom: 1px solid #ddd;'>{shift.TimeSlot.Label} ({shift.TimeSlot.StartTime:h:mm tt})</td>");
            body.AppendLine($"<td style='padding: 10px; border-bottom: 1px solid #ddd;'>{shift.Role}</td>");
            body.AppendLine($"<td style='padding: 10px; border-bottom: 1px solid #ddd;'>");
            body.AppendLine($"<a href='{_tokenService.GenerateActionUrl(confirmToken)}' style='color: green; margin-right: 10px;'>Confirm</a>");
            body.AppendLine($"<a href='{_tokenService.GenerateActionUrl(declineToken)}' style='color: orange;'>Decline</a>");
            body.AppendLine("</td>");
            body.AppendLine("</tr>");
        }

        body.AppendLine("</table>");
        body.AppendLine("<p style='margin-top: 20px;'>Please confirm your shifts as soon as possible.</p>");
        body.AppendLine("<p>If you cannot work a shift, please decline it so we can find coverage.</p>");
        body.AppendLine($"<p style='color: #666; font-size: 12px;'>Questions? Contact the Intergroup office.</p>");

        await SendEmailAsync(volunteer.Email, subject, body.ToString());
    }

    public async Task SendReminderEmailAsync(Volunteer volunteer, List<Shift> unconfirmedShifts)
    {
        if (!unconfirmedShifts.Any()) return;

        var subject = "Reminder: Please Confirm Your Volunteer Shifts";

        var body = new StringBuilder();
        body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
        body.AppendLine("<p>You have unconfirmed shifts coming up. Please confirm or decline:</p>");
        body.AppendLine("<ul>");

        foreach (var shift in unconfirmedShifts.OrderBy(s => s.Date))
        {
            var confirmToken = await _tokenService.CreateTokenAsync(shift.Id, volunteer.Id, TokenAction.Confirm);
            var declineToken = await _tokenService.CreateTokenAsync(shift.Id, volunteer.Id, TokenAction.Decline);

            body.AppendLine($"<li><strong>{shift.Date:dddd, MMM d}</strong> - {shift.TimeSlot.Label} ({shift.Role})");
            body.AppendLine($"<br/><a href='{_tokenService.GenerateActionUrl(confirmToken)}'>Confirm</a> | ");
            body.AppendLine($"<a href='{_tokenService.GenerateActionUrl(declineToken)}'>Decline</a></li>");
        }

        body.AppendLine("</ul>");
        body.AppendLine("<p>Shifts not confirmed may be reassigned 24 hours before they start.</p>");

        await SendEmailAsync(volunteer.Email, subject, body.ToString());
    }

    public async Task Send24HourReminderAsync(Volunteer volunteer, Shift shift)
    {
        var subject = $"Reminder: Volunteer Shift Tomorrow at {shift.TimeSlot.StartTime:h:mm tt}";

        var body = new StringBuilder();
        body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
        body.AppendLine($"<p>This is a reminder that you have a volunteer shift tomorrow:</p>");
        body.AppendLine($"<div style='background-color: #f0f0f0; padding: 15px; margin: 15px 0;'>");
        body.AppendLine($"<strong>{shift.Date:dddd, MMMM d, yyyy}</strong><br/>");
        body.AppendLine($"{shift.TimeSlot.Label}: {shift.TimeSlot.StartTime:h:mm tt} - {shift.TimeSlot.StartTime.AddMinutes(shift.TimeSlot.DurationMinutes):h:mm tt}<br/>");
        body.AppendLine($"Role: {shift.Role}");
        body.AppendLine("</div>");

        if (shift.Role == ShiftRole.Phone)
        {
            body.AppendLine("<p>Calls will be forwarded to your phone during your shift.</p>");
        }
        else
        {
            body.AppendLine("<p>Please arrive at the Intergroup office on time.</p>");
        }

        var cancelToken = await _tokenService.CreateTokenAsync(shift.Id, volunteer.Id, TokenAction.Cancel, 1);
        body.AppendLine($"<p>If you can no longer work this shift, <a href='{_tokenService.GenerateActionUrl(cancelToken)}'>click here to cancel</a>.</p>");

        await SendEmailAsync(volunteer.Email, subject, body.ToString());
    }

    public async Task SendShiftRequestReceivedAsync(Volunteer volunteer, Shift shift)
    {
        var subject = "Your Shift Request Has Been Received";

        var body = new StringBuilder();
        body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
        body.AppendLine($"<p>Thank you for volunteering! Your request for the following shift has been received:</p>");
        body.AppendLine($"<div style='background-color: #f0f0f0; padding: 15px; margin: 15px 0;'>");
        body.AppendLine($"<strong>{shift.Date:dddd, MMMM d, yyyy}</strong><br/>");
        body.AppendLine($"{shift.TimeSlot.Label}: {shift.TimeSlot.StartTime:h:mm tt}<br/>");
        body.AppendLine($"Role: {shift.Role}");
        body.AppendLine("</div>");
        body.AppendLine("<p>An administrator will review your request and you'll receive an email once it's approved.</p>");

        await SendEmailAsync(volunteer.Email, subject, body.ToString());
    }

    public async Task SendShiftApprovedAsync(Volunteer volunteer, Shift shift)
    {
        var subject = $"Shift Approved: {shift.Date:MMM d} at {shift.TimeSlot.StartTime:h:mm tt}";

        var confirmToken = await _tokenService.CreateTokenAsync(shift.Id, volunteer.Id, TokenAction.Confirm);
        var declineToken = await _tokenService.CreateTokenAsync(shift.Id, volunteer.Id, TokenAction.Decline);

        var body = new StringBuilder();
        body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
        body.AppendLine($"<p>Great news! Your shift request has been approved:</p>");
        body.AppendLine($"<div style='background-color: #d4edda; padding: 15px; margin: 15px 0;'>");
        body.AppendLine($"<strong>{shift.Date:dddd, MMMM d, yyyy}</strong><br/>");
        body.AppendLine($"{shift.TimeSlot.Label}: {shift.TimeSlot.StartTime:h:mm tt}<br/>");
        body.AppendLine($"Role: {shift.Role}");
        body.AppendLine("</div>");
        body.AppendLine($"<p><a href='{_tokenService.GenerateActionUrl(confirmToken)}' style='background-color: green; color: white; padding: 10px 20px; text-decoration: none;'>Confirm This Shift</a></p>");
        body.AppendLine($"<p>Can't make it? <a href='{_tokenService.GenerateActionUrl(declineToken)}'>Decline this shift</a></p>");

        await SendEmailAsync(volunteer.Email, subject, body.ToString());
    }

    public async Task SendShiftRejectedAsync(Volunteer volunteer, Shift shift)
    {
        var subject = $"Shift Request Update: {shift.Date:MMM d}";

        var body = new StringBuilder();
        body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
        body.AppendLine($"<p>Thank you for your interest in volunteering. Unfortunately, we were unable to accommodate your request for the following shift:</p>");
        body.AppendLine($"<div style='background-color: #f8d7da; padding: 15px; margin: 15px 0;'>");
        body.AppendLine($"<strong>{shift.Date:dddd, MMMM d, yyyy}</strong><br/>");
        body.AppendLine($"{shift.TimeSlot.Label}: {shift.TimeSlot.StartTime:h:mm tt}");
        body.AppendLine("</div>");
        body.AppendLine($"<p>This slot may have already been filled. Please check our <a href='{_configuration["App:BaseUrl"]}/shifts/open'>open shifts page</a> for other volunteer opportunities.</p>");
        body.AppendLine("<p>Thank you for your willingness to serve!</p>");

        await SendEmailAsync(volunteer.Email, subject, body.ToString());
    }

    public async Task SendShiftReopenedToAdminAsync(Shift shift)
    {
        var adminEmails = _configuration["App:AdminNotificationEmails"]?.Split(',') ?? Array.Empty<string>();
        if (!adminEmails.Any()) return;

        var subject = $"Shift Reopened: {shift.Date:MMM d} - {shift.TimeSlot.Label}";

        var body = new StringBuilder();
        body.AppendLine("<h2>Shift Coverage Needed</h2>");
        body.AppendLine("<p>The following shift has been automatically reopened because it was not confirmed:</p>");
        body.AppendLine($"<div style='background-color: #f8d7da; padding: 15px; margin: 15px 0;'>");
        body.AppendLine($"<strong>{shift.Date:dddd, MMMM d, yyyy}</strong><br/>");
        body.AppendLine($"{shift.TimeSlot.Label}: {shift.TimeSlot.StartTime:h:mm tt}<br/>");
        body.AppendLine($"Role: {shift.Role}");
        body.AppendLine("</div>");
        body.AppendLine("<p>Please review and take action to find coverage.</p>");

        foreach (var email in adminEmails.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            await SendEmailAsync(email.Trim(), subject, body.ToString());
        }
    }

    public async Task SendEscalationToBackupsAsync(Shift shift, List<Volunteer> backups)
    {
        var subject = $"Volunteer Needed: {shift.Date:MMM d} at {shift.TimeSlot.StartTime:h:mm tt}";

        foreach (var volunteer in backups)
        {
            var body = new StringBuilder();
            body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
            body.AppendLine("<p>We need coverage for an open shift. As a backup volunteer, you're being contacted first:</p>");
            body.AppendLine($"<div style='background-color: #fff3cd; padding: 15px; margin: 15px 0;'>");
            body.AppendLine($"<strong>{shift.Date:dddd, MMMM d, yyyy}</strong><br/>");
            body.AppendLine($"{shift.TimeSlot.Label}: {shift.TimeSlot.StartTime:h:mm tt}<br/>");
            body.AppendLine($"Role: {shift.Role}");
            body.AppendLine("</div>");

            var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:5001";
            body.AppendLine($"<p><a href='{baseUrl}/shifts/request/{shift.Id}' style='background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none;'>Request This Shift</a></p>");

            await SendEmailAsync(volunteer.Email, subject, body.ToString());
        }
    }

    public async Task SendEscalationToAllAsync(Shift shift, List<Volunteer> volunteers)
    {
        var subject = $"URGENT: Volunteer Needed {shift.Date:MMM d} at {shift.TimeSlot.StartTime:h:mm tt}";

        foreach (var volunteer in volunteers)
        {
            var body = new StringBuilder();
            body.AppendLine($"<h2>Hello {volunteer.Name},</h2>");
            body.AppendLine("<p><strong>We urgently need coverage for an open shift:</strong></p>");
            body.AppendLine($"<div style='background-color: #f8d7da; padding: 15px; margin: 15px 0;'>");
            body.AppendLine($"<strong>{shift.Date:dddd, MMMM d, yyyy}</strong><br/>");
            body.AppendLine($"{shift.TimeSlot.Label}: {shift.TimeSlot.StartTime:h:mm tt}<br/>");
            body.AppendLine($"Role: {shift.Role}");
            body.AppendLine("</div>");

            var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:5001";
            body.AppendLine($"<p><a href='{baseUrl}/shifts/request/{shift.Id}' style='background-color: #dc3545; color: white; padding: 10px 20px; text-decoration: none;'>I Can Help - Request This Shift</a></p>");

            await SendEmailAsync(volunteer.Email, subject, body.ToString());
        }
    }

    private async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        try
        {
            var message = new EmailMessage
            {
                From = $"{_fromName} <{_fromAddress}>",
                To = { to },
                Subject = subject,
                HtmlBody = WrapInTemplate(htmlBody)
            };

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
            throw;
        }
    }

    private string WrapInTemplate(string content)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
</head>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;'>
    {content}
    <hr style='margin-top: 30px; border: none; border-top: 1px solid #ddd;'>
    <p style='color: #666; font-size: 12px;'>
        This is an automated message from the Volunteer Shift Management System.<br>
        Please do not reply to this email.
    </p>
</body>
</html>";
    }
}
