using VolunteerCoordinator.Domain.Notifications;

namespace VolunteerCoordinator.Application.Ports;

public interface INotificationOutboxStore
{
    void AddNotificationIntent(NotificationIntent intent);
    Task<NotificationIntent?> GetNotificationIntentForUpdateAsync(
        Guid intentId,
        CancellationToken cancellationToken);

    Task<bool> HasEquivalentPendingIntentAsync(
        string eventKey,
        CancellationToken cancellationToken);
    Task<NotificationIntent?> GetEquivalentPendingIntentAsync(
        string eventKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationIntent>> GetNotificationIntentsForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationIntent>> GetNotificationIntentsAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDeliveryAttempt>> GetDeliveryAttemptsAsync(
        IReadOnlyCollection<Guid> intentIds,
        CancellationToken cancellationToken);
}
