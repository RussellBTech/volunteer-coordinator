using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Access;

public sealed class RecoveryTokenTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiryIsExactlyThirtyMinutesAndConsumptionIsSingleUse()
    {
        var token = RecoveryToken.Create(Guid.NewGuid(), Guid.NewGuid(), new byte[32], Created);

        Assert.True(token.IsUsable(Created.AddMinutes(30)));
        token.Consume(Created.AddMinutes(30));
        Assert.False(token.IsUsable(Created.AddMinutes(30)));
        Assert.Throws<DomainException>(() => token.Consume(Created.AddMinutes(30)));
    }
}
