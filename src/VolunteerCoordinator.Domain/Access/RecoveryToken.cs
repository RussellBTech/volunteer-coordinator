namespace VolunteerCoordinator.Domain.Access;

public sealed class RecoveryToken
{
    private RecoveryToken()
    {
    }

    private RecoveryToken(
        Guid volunteerId,
        Guid shiftSlotId,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (tokenHash.Length != 32)
        {
            throw new DomainException("A SHA-256 recovery-token hash is required.");
        }

        ValidateUtc(createdAtUtc);
        ValidateUtc(expiresAtUtc);
        if (expiresAtUtc != createdAtUtc.AddMinutes(30))
        {
            throw new DomainException("Recovery tokens must expire exactly 30 minutes after creation.");
        }

        Id = Guid.NewGuid();
        VolunteerId = volunteerId;
        ShiftSlotId = shiftSlotId;
        TokenHash = tokenHash.ToArray();
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid VolunteerId { get; private set; }

    public Guid ShiftSlotId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public DateTimeOffset? InvalidatedAtUtc { get; private set; }

    public static RecoveryToken Create(
        Guid volunteerId,
        Guid shiftSlotId,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc) =>
        new(volunteerId, shiftSlotId, tokenHash, createdAtUtc, createdAtUtc.AddMinutes(30));

    public bool IsUsable(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        return UsedAtUtc is null && InvalidatedAtUtc is null && nowUtc <= ExpiresAtUtc;
    }

    public void Consume(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (UsedAtUtc.HasValue || InvalidatedAtUtc.HasValue)
        {
            throw new DomainException("This recovery link is invalid or has expired.");
        }

        if (nowUtc > ExpiresAtUtc)
        {
            throw new DomainException("This recovery link is invalid or has expired.");
        }

        UsedAtUtc = nowUtc;
    }

    public bool Invalidate(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (InvalidatedAtUtc.HasValue)
        {
            return false;
        }

        InvalidatedAtUtc = nowUtc;
        return true;
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Recovery-token timestamps must be UTC.");
        }
    }
}
