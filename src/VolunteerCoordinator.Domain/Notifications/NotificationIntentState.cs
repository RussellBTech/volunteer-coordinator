namespace VolunteerCoordinator.Domain.Notifications;

public enum NotificationIntentState
{
    Pending,
    RetryScheduled,
    InFlight,
    Accepted,
    Delivered,
    Bounced,
    Complained,
    Failed,
    Cancelled
}
