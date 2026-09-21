namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class ResendWebhookOptions
{
    public TimeSpan TimestampTolerance { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxBodyBytes { get; set; } = 128 * 1024;
}
