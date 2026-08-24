namespace VolunteerCoordinator.Application.Notifications;

public sealed record NotificationMessage(Guid TransitionId, string Kind, string Destination);
