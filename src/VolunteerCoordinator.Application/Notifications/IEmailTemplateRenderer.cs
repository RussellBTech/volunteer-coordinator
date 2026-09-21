namespace VolunteerCoordinator.Application.Notifications;

public interface IEmailTemplateRenderer
{
    EmailTemplate Render(NotificationTemplateContext context, string kind);
}
