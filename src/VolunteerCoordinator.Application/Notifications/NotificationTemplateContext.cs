namespace VolunteerCoordinator.Application.Notifications;

public sealed record NotificationTemplateContext(
    string VolunteerName,
    string ShiftTitle,
    string SlotLabel,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string GroupTimeZoneId,
    string? Location,
    string? VolunteerInstructions,
    string Status,
    string? PrivateUrl,
    string? ReplyTo);
