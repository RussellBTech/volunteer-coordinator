using System.Net;
using System.Text;
using VolunteerCoordinator.Application.Notifications;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class SafeEmailTemplateRenderer : IEmailTemplateRenderer
{
    public EmailTemplate Render(NotificationTemplateContext context, string kind)
    {
        var start = FormatLocal(context.StartsAtUtc, context.GroupTimeZoneId);
        var end = FormatLocal(context.EndsAtUtc, context.GroupTimeZoneId);
        var subject = kind.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
                      kind.Contains("Reissue", StringComparison.OrdinalIgnoreCase)
            ? "Recover your volunteer commitment"
            : kind.Contains("Request", StringComparison.OrdinalIgnoreCase)
                ? "Your volunteer request"
                : "Your service commitment";
        var action = context.PrivateUrl is null
            ? string.Empty
            : $"\nPrivate link: {Plain(context.PrivateUrl)}\n";
        var text = $"Hello {Plain(context.VolunteerName)},\n\n" +
            $"This is a scheduling message about {Plain(context.ShiftTitle)} ({Plain(context.SlotLabel)}).\n" +
            $"When: {Plain(start)} to {Plain(end)} ({Plain(context.GroupTimeZoneId)}).\n" +
            (string.IsNullOrWhiteSpace(context.Location) ? string.Empty : $"Location: {Plain(context.Location)}\n") +
            (string.IsNullOrWhiteSpace(context.VolunteerInstructions) ? string.Empty : $"Instructions: {Plain(context.VolunteerInstructions)}\n") +
            $"Current status: {Plain(context.Status)}.\n" + action +
            "This message is for service scheduling only. Delivery means the mail server accepted it; it does not show that it was read.\n" +
            (string.IsNullOrWhiteSpace(context.ReplyTo) ? string.Empty : $"Reply to {Plain(context.ReplyTo)} for help.\n");
        var html = $"<p>Hello {Html(context.VolunteerName)},</p>" +
            $"<p>This is a scheduling message about <strong>{Html(context.ShiftTitle)}</strong> ({Html(context.SlotLabel)}).</p>" +
            $"<p><strong>When:</strong> {Html(start)} to {Html(end)} ({Html(context.GroupTimeZoneId)}).</p>" +
            (string.IsNullOrWhiteSpace(context.Location) ? string.Empty : $"<p><strong>Location:</strong> {Html(context.Location)}</p>") +
            (string.IsNullOrWhiteSpace(context.VolunteerInstructions) ? string.Empty : $"<p><strong>Instructions:</strong> {Html(context.VolunteerInstructions)}</p>") +
            $"<p><strong>Current status:</strong> {Html(context.Status)}.</p>" +
            (context.PrivateUrl is null ? string.Empty : $"<p><a href=\"{Html(context.PrivateUrl)}\">Open your private commitment link</a></p>") +
            "<p>This message is for service scheduling only. Delivery means the mail server accepted it; it does not show that it was read.</p>" +
            (string.IsNullOrWhiteSpace(context.ReplyTo) ? string.Empty : $"<p>Reply to {Html(context.ReplyTo)} for help.</p>");
        return new EmailTemplate(subject, text, html);
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string Plain(string? value) =>
        (value ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    private static string FormatLocal(DateTimeOffset value, string timeZoneId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (TimeZoneNotFoundException)
        {
            return value.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidTimeZoneException)
        {
            return value.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
