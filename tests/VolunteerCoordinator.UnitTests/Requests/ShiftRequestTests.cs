using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Requests;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Requests;

public sealed class ShiftRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Approve_ResolvesPendingRequestOnce()
    {
        var request = Create();
        request.Approve("COORDINATOR@EXAMPLE.ORG", Now.AddMinutes(1));

        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.Throws<DomainException>(() => request.Reject("COORDINATOR@EXAMPLE.ORG", Now.AddMinutes(2)));
    }

    [Fact]
    public void StatusToken_RemainsReadOnlyReusableUntilExpiry()
    {
        var request = Create();

        Assert.True(request.IsStatusTokenUsable(Now.AddDays(29)));
        Assert.True(request.IsStatusTokenUsable(Now.AddDays(30)));
        Assert.False(request.IsStatusTokenUsable(Now.AddDays(30).AddTicks(1)));
    }

    private static ShiftRequest Create() =>
        ShiftRequest.Create(Guid.NewGuid(), Guid.NewGuid(), new byte[32], Now, Now.AddDays(30));
}
