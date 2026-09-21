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


    private static ShiftRequest Create() =>
        ShiftRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
}
