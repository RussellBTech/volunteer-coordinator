using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Domain.Commitments;

public sealed class RecurringCommitment
{
    private RecurringCommitment()
    {
    }

    private RecurringCommitment(
        Guid seriesId,
        Guid revisionId,
        Guid volunteerId,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        DateOnly endLocalDate,
        SignupPolicy sourcePolicy,
        Guid? sourceRequestId,
        DateTimeOffset createdAtUtc)
    {
        ValidateRange(effectiveLocalDate, endLocalDate);
        ValidateUtc(createdAtUtc);
        Id = Guid.NewGuid();
        SeriesId = seriesId;
        RevisionId = revisionId;
        VolunteerId = volunteerId;
        RoleKind = roleKind;
        RolePosition = rolePosition;
        EffectiveLocalDate = effectiveLocalDate;
        EndLocalDate = endLocalDate;
        SourcePolicy = sourcePolicy;
        SourceRequestId = sourceRequestId;
        State = sourcePolicy == SignupPolicy.DirectClaim
            ? RecurringCommitmentState.Active
            : RecurringCommitmentState.AwaitingConfirmation;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid RevisionId { get; private set; }
    public Guid VolunteerId { get; private set; }
    public SlotKind RoleKind { get; private set; }
    public int RolePosition { get; private set; }
    public DateOnly EffectiveLocalDate { get; private set; }
    public DateOnly EndLocalDate { get; private set; }
    public SignupPolicy SourcePolicy { get; private set; }
    public Guid? SourceRequestId { get; private set; }
    public RecurringCommitmentState State { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public DateOnly? WithdrawalEffectiveLocalDate { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpenForOverlap => State is RecurringCommitmentState.AwaitingConfirmation or RecurringCommitmentState.Active;

    public static RecurringCommitment Create(
        Guid seriesId,
        Guid revisionId,
        Guid volunteerId,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        DateOnly endLocalDate,
        SignupPolicy sourcePolicy,
        Guid? sourceRequestId,
        DateTimeOffset createdAtUtc) => new(
            seriesId,
            revisionId,
            volunteerId,
            roleKind,
            rolePosition,
            effectiveLocalDate,
            endLocalDate,
            sourcePolicy,
            sourceRequestId,
            createdAtUtc);

    public void Confirm(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (State != RecurringCommitmentState.AwaitingConfirmation)
        {
            if (State == RecurringCommitmentState.Active)
            {
                return;
            }

            throw new DomainException("This recurring commitment is no longer awaiting confirmation.");
        }

        State = RecurringCommitmentState.Active;
        ConfirmedAtUtc = nowUtc;
        Version++;
    }
    public void MoveToRevision(Guid revisionId, DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (revisionId == Guid.Empty)
        {
            throw new DomainException("A recurring revision is required.");
        }

        if (RevisionId == revisionId)
        {
            return;
        }

        RevisionId = revisionId;
        Version++;
    }

    public void ResolveStranded(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (State is RecurringCommitmentState.Completed or RecurringCommitmentState.Withdrawn)
        {
            return;
        }

        State = RecurringCommitmentState.Completed;
        Version++;
    }


    public void Withdraw(DateOnly effectiveLocalDate, DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (State is RecurringCommitmentState.Withdrawn or RecurringCommitmentState.Completed)
        {
            return;
        }

        if (effectiveLocalDate < EffectiveLocalDate || effectiveLocalDate > EndLocalDate)
        {
            throw new DomainException("Choose a future occurrence inside this recurring commitment.");
        }

        State = RecurringCommitmentState.Withdrawn;
        WithdrawnAtUtc = nowUtc;
        WithdrawalEffectiveLocalDate = effectiveLocalDate;
        Version++;
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (State == RecurringCommitmentState.Active)
        {
            State = RecurringCommitmentState.Completed;
            Version++;
        }
    }

    private static void ValidateRange(DateOnly effectiveLocalDate, DateOnly endLocalDate)
    {
        if (endLocalDate < effectiveLocalDate)
        {
            throw new DomainException("A recurring commitment must have an end date after its first occurrence.");
        }

        var days = endLocalDate.DayNumber - effectiveLocalDate.DayNumber + 1;
        if (days is < 28 or > 182)
        {
            throw new DomainException("A recurring commitment must cover between four and twenty-six weeks.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Recurring commitment timestamps must be UTC.");
        }
    }
}
