namespace VolunteerCoordinator.Domain.Commitments;

public enum RecurringCommitmentState
{
    AwaitingConfirmation = 0,
    Active = 1,
    Withdrawn = 2,
    Completed = 3
}
