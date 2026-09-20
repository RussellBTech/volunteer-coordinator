using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Settings;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Settings;

public sealed class GroupSettingsTests
{
    [Fact]
    public void CreateUsesSingletonIdentityAndTrimsZone()
    {
        var settings = GroupSettings.Create("  America/New_York  ");

        Assert.Equal(GroupSettings.SingletonId, settings.Id);
        Assert.Equal("America/New_York", settings.TimeZoneId);
    }

    [Fact]
    public void ConfigureChangesOnlyTheZoneIdentity()
    {
        var settings = GroupSettings.Create("America/New_York");

        settings.Configure(" Europe/London ");

        Assert.Equal(GroupSettings.SingletonId, settings.Id);
        Assert.Equal("Europe/London", settings.TimeZoneId);
    }

    [Fact]
    public void CreateRejectsMissingZone()
    {
        var exception = Assert.Throws<DomainException>(() => GroupSettings.Create("  "));

        Assert.Contains("time-zone", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
