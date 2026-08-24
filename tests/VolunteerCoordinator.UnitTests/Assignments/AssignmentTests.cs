using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Assignments;

public sealed class AssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Confirm_TransitionsAssignedToConfirmed()
    {
        var assignment = Create();
        assignment.Confirm(Now.AddMinutes(1));

        Assert.Equal(AssignmentStatus.Confirmed, assignment.Status);
        Assert.True(assignment.IsActive);
        Assert.NotNull(assignment.ConfirmedAtUtc);
    }

    [Fact]
    public void Decline_ReopensSlotByEndingAssignment()
    {
        var assignment = Create();
        assignment.Decline(Now.AddMinutes(1));

        Assert.Equal(AssignmentStatus.Declined, assignment.Status);
        Assert.False(assignment.IsActive);
        Assert.NotNull(assignment.EndedAtUtc);
    }

    [Fact]
    public void Cancel_EndsConfirmedAssignment()
    {
        var assignment = Create();
        assignment.Confirm(Now.AddMinutes(1));
        assignment.Cancel(Now.AddMinutes(2));

        Assert.Equal(AssignmentStatus.Cancelled, assignment.Status);
        Assert.False(assignment.IsActive);
    }

    [Fact]
    public void Reassign_EndsPreviousAssignment()
    {
        var assignment = Create();
        assignment.Reassign(Now.AddMinutes(1));

        Assert.Equal(AssignmentStatus.Reassigned, assignment.Status);
        Assert.False(assignment.IsActive);
    }

    [Fact]
    public void Confirm_CannotBeAppliedTwice()
    {
        var assignment = Create();
        assignment.Confirm(Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => assignment.Confirm(Now.AddMinutes(2)));
    }

    private static Assignment Create() => Assignment.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "COORDINATOR@EXAMPLE.ORG", Now);
}
