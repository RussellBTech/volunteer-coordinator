namespace VolunteerCoordinator.Application.Notifications;

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "NotificationDelivery";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public int BatchSize { get; set; } = 25;

    public bool IsValid() => PollInterval > TimeSpan.Zero && BatchSize is >= 1 and <= 25;
}
