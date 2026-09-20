namespace VolunteerCoordinator.Domain.Settings;

public sealed class GroupSettings
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private GroupSettings()
    {
    }

    private GroupSettings(string timeZoneId)
    {
        Id = SingletonId;
        TimeZoneId = NormalizeTimeZoneId(timeZoneId);
    }

    public Guid Id { get; private set; }

    public string TimeZoneId { get; private set; } = string.Empty;

    public uint Version { get; private set; }

    public static GroupSettings Create(string timeZoneId) => new(timeZoneId);

    public void Configure(string timeZoneId)
    {
        TimeZoneId = NormalizeTimeZoneId(timeZoneId);
    }

    private static string NormalizeTimeZoneId(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new DomainException("A group time-zone identifier is required.");
        }

        return timeZoneId.Trim();
    }
}
