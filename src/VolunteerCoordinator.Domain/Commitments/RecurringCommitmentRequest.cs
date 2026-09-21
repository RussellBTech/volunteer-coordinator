using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Domain.Commitments;

public sealed class RecurringCommitmentRequest
{
    private RecurringCommitmentRequest()
    {
    }

    private RecurringCommitmentRequest(
        Guid seriesId,
        Guid revisionId,
        Guid volunteerId,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        DateOnly endLocalDate,
        SignupPolicy sourcePolicy,
        DateTimeOffset requestedAtUtc)
    {
        ValidateRange(effectiveLocalDate, endLocalDate);
        ValidateUtc(requestedAtUtc);
        Id = Guid.NewGuid();
        SeriesId = seriesId;
        RevisionId = revisionId;
        VolunteerId = volunteerId;
        RoleKind = roleKind;
        RolePosition = rolePosition;
        EffectiveLocalDate = effectiveLocalDate;
        EndLocalDate = endLocalDate;
        SourcePolicy = sourcePolicy;
        RequestedAtUtc = requestedAtUtc;
        Status = RecurringCommitmentRequestStatus.Pending;
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
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public RecurringCommitmentRequestStatus Status { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public string? ResolvedByCoordinatorEmail { get; private set; }
    public Guid? RecurringCommitmentId { get; private set; }
    public uint Version { get; private set; }

    public static RecurringCommitmentRequest Create(
        Guid seriesId,
        Guid revisionId,
        Guid volunteerId,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        DateOnly endLocalDate,
        SignupPolicy sourcePolicy,
        DateTimeOffset requestedAtUtc) => new(
            seriesId,
            revisionId,
            volunteerId,
            roleKind,
            rolePosition,
            effectiveLocalDate,
            endLocalDate,
            sourcePolicy,
            requestedAtUtc);

    public bool IsPending => Status == RecurringCommitmentRequestStatus.Pending;

    public void Approve(string coordinatorEmail, DateTimeOffset nowUtc, Guid commitmentId)
    {
        Resolve(RecurringCommitmentRequestStatus.Approved, coordinatorEmail, nowUtc);
        RecurringCommitmentId = commitmentId;
        Version++;
    }

    public void Reject(string coordinatorEmail, DateTimeOffset nowUtc) =>
        Resolve(RecurringCommitmentRequestStatus.Rejected, coordinatorEmail, nowUtc);

    public void Supersede(string coordinatorEmail, DateTimeOffset nowUtc) =>
        Resolve(RecurringCommitmentRequestStatus.Superseded, coordinatorEmail, nowUtc);

    private void Resolve(
        RecurringCommitmentRequestStatus status,
        string coordinatorEmail,
        DateTimeOffset nowUtc)
    {
        if (Status != RecurringCommitmentRequestStatus.Pending)
        {
            throw new DomainException("Only a pending recurring request can be resolved.");
        }

        if (string.IsNullOrWhiteSpace(coordinatorEmail))
        {
            throw new DomainException("A coordinator identity is required.");
        }

        ValidateUtc(nowUtc);
        Status = status;
        ResolvedAtUtc = nowUtc;
        ResolvedByCoordinatorEmail = coordinatorEmail.Trim().ToUpperInvariant();
        Version++;
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
