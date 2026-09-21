using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Commitments;
using VolunteerCoordinator.Domain.Schedules;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Commitments;

public sealed class Issue21CommitmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Existing_shift_defaults_to_approval_required()
    {
        var shift = Shift.Create(
            "Community service",
            null,
            null,
            Now.AddDays(1),
            Now.AddDays(1).AddHours(2),
            1);

        Assert.Equal(SignupPolicy.ApprovalRequired, shift.SignupPolicy);
    }

    [Fact]
    public void Direct_claim_assignment_is_confirmed_at_claim_time()
    {
        var assignment = Assignment.DirectClaim(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now);

        Assert.Equal(AssignmentStatus.Confirmed, assignment.Status);
        Assert.Equal(Now, assignment.AssignedAtUtc);
        Assert.Equal(Now, assignment.ConfirmedAtUtc);
    }

    [Fact]
    public void Recurring_commitment_rejects_unbounded_ranges()
    {
        var exception = Assert.Throws<DomainException>(() => RecurringCommitment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SlotKind.Primary,
            1,
            new DateOnly(2026, 10, 1),
            new DateOnly(2027, 4, 1),
            SignupPolicy.DirectClaim,
            null,
            Now));

        Assert.Contains("four", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Withdrawal_is_idempotent_and_preserves_boundary()
    {
        var start = new DateOnly(2026, 10, 1);
        var commitment = RecurringCommitment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SlotKind.Primary,
            1,
            start,
            start.AddDays(27),
            SignupPolicy.DirectClaim,
            null,
            Now);

        commitment.Withdraw(start.AddDays(7), Now.AddMinutes(1));
        commitment.Withdraw(start.AddDays(7), Now.AddMinutes(2));

        Assert.Equal(RecurringCommitmentState.Withdrawn, commitment.State);
        Assert.Equal(start.AddDays(7), commitment.WithdrawalEffectiveLocalDate);
        Assert.Equal(Now.AddMinutes(1), commitment.WithdrawnAtUtc);
    }

    [Fact]
    public void Recurring_join_keeps_replacement_history()
    {
        var join = RecurringCommitmentOccurrence.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            RecurringCommitmentOccurrenceState.Assigned,
            Guid.NewGuid());

        join.MarkReplaced("Moved during an explicit revision handoff.");

        Assert.Equal(RecurringCommitmentOccurrenceState.Replaced, join.State);
        Assert.Equal("Moved during an explicit revision handoff.", join.Reason);
        Assert.NotNull(join.AssignmentId);
    }
    [Fact]
    public void Recurring_capability_read_grace_ends_seven_days_after_final_occurrence()
    {
        var capability = RecurringCommitmentCapability.Create(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            new byte[32],
            Now);
        var finalEnd = Now.AddDays(3);

        Assert.True(capability.IsReadable(finalEnd.AddDays(7), finalEnd));
        Assert.False(capability.IsReadable(finalEnd.AddDays(7).AddTicks(1), finalEnd));
    }

    [Fact]
    public void Stranded_awaiting_commitment_resolves_without_reopening_access()
    {
        var commitment = RecurringCommitment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SlotKind.Primary,
            1,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 28),
            SignupPolicy.ApprovalRequired,
            Guid.NewGuid(),
            Now);

        commitment.ResolveStranded(Now.AddMinutes(1));

        Assert.Equal(RecurringCommitmentState.Completed, commitment.State);
    }
}
