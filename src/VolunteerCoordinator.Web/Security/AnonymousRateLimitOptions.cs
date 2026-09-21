namespace VolunteerCoordinator.Web.Security;

public sealed class AnonymousRateLimitOptions
{
    public const string SectionName = "AnonymousRateLimits";

    public TierOptions RequestMutation { get; set; } = new()
    {
        PermitLimit = 5,
        Window = TimeSpan.FromMinutes(1)
    };

    public TierOptions PrivateTokenRead { get; set; } = new()
    {
        PermitLimit = 30,
        Window = TimeSpan.FromMinutes(1)
    };

    public TierOptions AssignmentActionMutation { get; set; } = new()
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(1)
    };
    public TierOptions Recovery { get; set; } = new()
    {
        PermitLimit = 5,
        Window = TimeSpan.FromMinutes(15)
    };

    public bool IsValid() =>
        IsValid(RequestMutation) &&
        IsValid(PrivateTokenRead) &&
        IsValid(AssignmentActionMutation) &&
        IsValid(Recovery);

    private static bool IsValid(TierOptions? tier) =>
        tier is not null &&
        tier.PermitLimit > 0 &&
        tier.Window > TimeSpan.Zero;

    public sealed class TierOptions
    {
        public int PermitLimit { get; set; }

        public TimeSpan Window { get; set; }
    }
}
