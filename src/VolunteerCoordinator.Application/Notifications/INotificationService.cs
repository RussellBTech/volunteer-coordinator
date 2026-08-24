namespace VolunteerCoordinator.Application.Notifications;

public interface INotificationService
{
    Task<NotificationResult> RecordAndSendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
