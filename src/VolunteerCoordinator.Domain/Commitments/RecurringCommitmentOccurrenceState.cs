namespace VolunteerCoordinator.Domain.Commitments;

public enum RecurringCommitmentOccurrenceState
{
    Assigned = 0,
    Confirmed = 1,
    SkippedException = 2,
    Withdrawn = 3,
    Replaced = 4
}
