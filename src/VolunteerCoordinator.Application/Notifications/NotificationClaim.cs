using VolunteerCoordinator.Domain.Notifications;

namespace VolunteerCoordinator.Application.Notifications;

public sealed record NotificationClaim(
    NotificationIntent Intent,
    NotificationDeliveryAttempt Attempt,
    Guid ClaimOwnerToken,
    uint ClaimVersion);
