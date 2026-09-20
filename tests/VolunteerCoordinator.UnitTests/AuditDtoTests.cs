using VolunteerCoordinator.Application.Models;
using Xunit;

namespace VolunteerCoordinator.UnitTests;

public sealed class AuditDtoTests
{
    [Theory]
    [InlineData("volunteer:4d6f6c75-6e74-6565-723a-313233343536")]
    [InlineData("VOLUNTEER:4d6f6c75-6e74-6565-723a-313233343536")]
    [InlineData("volunteer-token")]
    public void VolunteerActorsUseNeutralDisplay(string actor)
    {
        var audit = new AuditDto(
            DateTimeOffset.UtcNow,
            actor,
            "RequestSubmitted",
            "ShiftRequest",
            Guid.NewGuid(),
            "{}");

        Assert.Equal("Volunteer", audit.ActorDisplay);
        Assert.DoesNotContain("4d6f6c75", audit.ActorDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoordinatorActorRemainsHumanReadable()
    {
        var audit = new AuditDto(
            DateTimeOffset.UtcNow,
            "COORDINATOR@EXAMPLE.ORG",
            "ShiftPublished",
            "Shift",
            Guid.NewGuid(),
            "{}");

        Assert.Equal("COORDINATOR@EXAMPLE.ORG", audit.ActorDisplay);
    }
}
