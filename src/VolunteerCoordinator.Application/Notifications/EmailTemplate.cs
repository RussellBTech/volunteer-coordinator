namespace VolunteerCoordinator.Application.Notifications;

public sealed record EmailTemplate(
    string Subject,
    string TextBody,
    string HtmlBody);
