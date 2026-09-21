using VolunteerCoordinator.Application.Notifications;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class OutboxNotificationService : INotificationService
{
    public Task<NotificationResult> RecordAndSendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken) =>
        Task.FromResult(new NotificationResult(true, null));
}
