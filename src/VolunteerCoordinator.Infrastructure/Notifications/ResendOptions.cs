namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public string? ApiKey { get; set; }

    public string? WebhookSecret { get; set; }

    public bool IsValid() => !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(WebhookSecret);
}
