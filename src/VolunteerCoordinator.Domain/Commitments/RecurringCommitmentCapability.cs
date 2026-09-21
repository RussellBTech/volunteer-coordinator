namespace VolunteerCoordinator.Domain.Commitments;

public sealed class RecurringCommitmentCapability
{
    private RecurringCommitmentCapability()
    {
    }

    private RecurringCommitmentCapability(
        Guid volunteerId,
        Guid? requestId,
        Guid? commitmentId,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc)
    {
        if (tokenHash.Length != 32)
        {
            throw new DomainException("A SHA-256 recurring capability hash is required.");
        }

        if (requestId is null && commitmentId is null)
        {
            throw new DomainException("A recurring capability must belong to a request or commitment.");
        }

        ValidateUtc(createdAtUtc);
        Id = Guid.NewGuid();
        VolunteerId = volunteerId;
        RequestId = requestId;
        CommitmentId = commitmentId;
        TokenHash = tokenHash.ToArray();
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid VolunteerId { get; private set; }
    public Guid? RequestId { get; private set; }
    public Guid? CommitmentId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? InvalidatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public static RecurringCommitmentCapability Create(
        Guid volunteerId,
        Guid? requestId,
        Guid? commitmentId,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc) => new(
            volunteerId,
            requestId,
            commitmentId,
            tokenHash,
            createdAtUtc);

    public bool IsReadable(DateTimeOffset nowUtc, DateTimeOffset finalOccurrenceEndsAtUtc)
    {
        ValidateUtc(nowUtc);
        ValidateUtc(finalOccurrenceEndsAtUtc);
        return IsActive && nowUtc <= finalOccurrenceEndsAtUtc.AddDays(7);
    }

    public bool IsActive => InvalidatedAtUtc is null;

    public void AttachCommitment(Guid commitmentId)
    {
        if (commitmentId == Guid.Empty)
        {
            throw new DomainException("A recurring commitment is required.");
        }

        CommitmentId = commitmentId;
        Version++;
    }

    public bool Invalidate(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (InvalidatedAtUtc.HasValue)
        {
            return false;
        }

        InvalidatedAtUtc = nowUtc;
        Version++;
        return true;
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Recurring capability timestamps must be UTC.");
        }
    }
}
