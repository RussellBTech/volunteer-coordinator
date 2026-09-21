namespace VolunteerCoordinator.Domain.Notifications;

public sealed class ResendWebhookReceipt
{
    private ResendWebhookReceipt()
    {
    }

    private ResendWebhookReceipt(
        string svixId,
        string eventType,
        string providerMessageId,
        DateTimeOffset providerOccurredAtUtc,
        DateTimeOffset processedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(svixId) || svixId.Trim().Length > 200)
        {
            throw new DomainException("A bounded webhook event identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(eventType) || eventType.Trim().Length > 100)
        {
            throw new DomainException("A bounded webhook event type is required.");
        }

        if (string.IsNullOrWhiteSpace(providerMessageId) || providerMessageId.Trim().Length > 200)
        {
            throw new DomainException("A bounded provider message identifier is required.");
        }

        if (providerOccurredAtUtc.Offset != TimeSpan.Zero || processedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Webhook timestamps must be UTC.");
        }

        Id = Guid.NewGuid();
        SvixId = svixId.Trim();
        EventType = eventType.Trim();
        ProviderMessageId = providerMessageId.Trim();
        ProviderOccurredAtUtc = providerOccurredAtUtc;
        ProcessedAtUtc = processedAtUtc;
    }

    public Guid Id { get; private set; }

    public string SvixId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public string ProviderMessageId { get; private set; } = string.Empty;

    public DateTimeOffset ProviderOccurredAtUtc { get; private set; }

    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public static ResendWebhookReceipt Create(
        string svixId,
        string eventType,
        string providerMessageId,
        DateTimeOffset providerOccurredAtUtc,
        DateTimeOffset processedAtUtc) =>
        new(svixId, eventType, providerMessageId, providerOccurredAtUtc, processedAtUtc);
}
