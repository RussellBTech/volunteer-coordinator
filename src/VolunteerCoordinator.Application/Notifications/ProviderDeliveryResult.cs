namespace VolunteerCoordinator.Application.Notifications;

public sealed record ProviderDeliveryResult(
    bool Accepted,
    bool Transient,
    string Category,
    string? ProviderMessageId = null,
    DateTimeOffset? RetryAfterUtc = null);
