using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Access;

public sealed class VolunteerAccessCapabilityTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReadGraceIncludesExactSevenDayBoundaryAndExcludesTheNextTick()
    {
        var capability = VolunteerAccessCapability.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new byte[32],
            Created,
            CapabilityIssuedReason.Request);
        var shiftEnd = Created.AddDays(30);

        Assert.True(capability.IsUsable(Created.AddDays(37), shiftEnd, true, true, false));
        Assert.False(capability.IsUsable(Created.AddDays(37).AddTicks(1), shiftEnd, true, true, false));
    }

    [Fact]
    public void InvalidationIsIdempotentAndRemovesUsability()
    {
        var capability = VolunteerAccessCapability.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new byte[32],
            Created,
            CapabilityIssuedReason.Recovery);

        Assert.True(capability.Invalidate(Created.AddMinutes(1)));
        Assert.False(capability.Invalidate(Created.AddMinutes(2)));
        Assert.False(capability.IsUsable(Created.AddMinutes(2), Created.AddDays(1), true, true, false));
    }
}
