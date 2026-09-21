namespace VolunteerCoordinator.Domain.Commitments;

public sealed class RecurringCommitmentOccurrence
{
    private RecurringCommitmentOccurrence()
    {
    }

    private RecurringCommitmentOccurrence(
        Guid commitmentId,
        Guid recurringOccurrenceId,
        RecurringCommitmentOccurrenceState state,
        Guid? assignmentId,
        string? reason)
    {
        Id = Guid.NewGuid();
        CommitmentId = commitmentId;
        RecurringOccurrenceId = recurringOccurrenceId;
        State = state;
        AssignmentId = assignmentId;
        Reason = NormalizeReason(reason);
    }

    public Guid Id { get; private set; }
    public Guid CommitmentId { get; private set; }
    public Guid RecurringOccurrenceId { get; private set; }
    public Guid? AssignmentId { get; private set; }
    public RecurringCommitmentOccurrenceState State { get; private set; }
    public string? Reason { get; private set; }
    public uint Version { get; private set; }

    public static RecurringCommitmentOccurrence Create(
        Guid commitmentId,
        Guid recurringOccurrenceId,
        RecurringCommitmentOccurrenceState state,
        Guid? assignmentId = null,
        string? reason = null) => new(
            commitmentId,
            recurringOccurrenceId,
            state,
            assignmentId,
            reason);

    public void SetAssignment(Guid assignmentId, RecurringCommitmentOccurrenceState state)
    {
        if (assignmentId == Guid.Empty)
        {
            throw new DomainException("A concrete assignment is required.");
        }

        AssignmentId = assignmentId;
        State = state;
        Reason = null;
        Version++;
    }

    public void MarkSkipped(string reason)
    {
        State = RecurringCommitmentOccurrenceState.SkippedException;
        AssignmentId = null;
        Reason = NormalizeReason(reason) ?? throw new DomainException("A skipped occurrence needs a visible reason.");
        Version++;
    }

    public void MarkWithdrawn(string? reason = null)
    {
        State = RecurringCommitmentOccurrenceState.Withdrawn;
        Reason = NormalizeReason(reason);
        Version++;
    }

    public void MarkReplaced(string? reason = null)
    {
        State = RecurringCommitmentOccurrenceState.Replaced;
        Reason = NormalizeReason(reason);
        Version++;
    }

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var normalized = reason.Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }
}
