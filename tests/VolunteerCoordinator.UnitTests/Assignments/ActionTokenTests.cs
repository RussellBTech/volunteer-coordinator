using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Assignments;

public sealed class ActionTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Consume_IsSingleUse()
    {
        var token = Create(Now.AddDays(1));
        token.Consume(Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => token.Consume(Now.AddMinutes(2)));
    }

    [Fact]
    public void Consume_RejectsExpiredTokenWithoutUsingIt()
    {
        var token = Create(Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => token.Consume(Now.AddMinutes(2)));
        Assert.Null(token.UsedAtUtc);
    }

    [Fact]
    public void Invalidate_ConsumesRegeneratedToken()
    {
        var token = Create(Now.AddDays(1));
        token.Invalidate(Now.AddMinutes(1));

        Assert.False(token.IsUsable(Now.AddMinutes(2)));
    }

    private static ActionToken Create(DateTimeOffset expiresAtUtc) =>
        ActionToken.Create(Guid.NewGuid(), VolunteerAction.Confirm, new byte[32], Now, expiresAtUtc);
}
