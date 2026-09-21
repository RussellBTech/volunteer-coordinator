namespace VolunteerCoordinator.Domain.Notifications;

public sealed class NotificationDeliveryAttempt
{
    private NotificationDeliveryAttempt()
    {
    }

    private NotificationDeliveryAttempt(Guid notificationIntentId, int ordinal, DateTimeOffset startedAtUtc)
    {
        if (ordinal is < 1 or > 5)
        {
            throw new DomainException("Notification attempts must be numbered one through five.");
        }

        if (startedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Notification timestamps must be UTC.");
        }

        Id = Guid.NewGuid();
        NotificationIntentId = notificationIntentId;
        Ordinal = ordinal;
        StartedAtUtc = startedAtUtc;
        IdempotencyKey = $"notification/{Id:N}";
    }

    public Guid Id { get; private set; }

    public Guid NotificationIntentId { get; private set; }

    public int Ordinal { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string? OutcomeCategory { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public static NotificationDeliveryAttempt Create(
        Guid notificationIntentId,
        int ordinal,
        DateTimeOffset startedAtUtc) =>
        new(notificationIntentId, ordinal, startedAtUtc);

    public void Complete(
        DateTimeOffset completedAtUtc,
        string outcomeCategory,
        string? providerMessageId = null)
    {
        if (completedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Notification timestamps must be UTC.");
        }

        CompletedAtUtc = completedAtUtc;
        OutcomeCategory = Normalize(outcomeCategory);
        ProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId)
            ? null
            : providerMessageId.Trim()[..Math.Min(providerMessageId.Trim().Length, 200)];
    }

    private static string Normalize(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }
}
