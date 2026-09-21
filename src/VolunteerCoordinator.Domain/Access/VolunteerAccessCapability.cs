namespace VolunteerCoordinator.Domain.Access;

public sealed class VolunteerAccessCapability
{
    private VolunteerAccessCapability()
    {
    }

    private VolunteerAccessCapability(
        Guid shiftSlotId,
        Guid volunteerId,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc,
        CapabilityIssuedReason issuedReason)
    {
        if (tokenHash.Length != 32)
        {
            throw new DomainException("A SHA-256 capability hash is required.");
        }

        ValidateUtc(createdAtUtc);
        Id = Guid.NewGuid();
        ShiftSlotId = shiftSlotId;
        VolunteerId = volunteerId;
        TokenHash = tokenHash.ToArray();
        CreatedAtUtc = createdAtUtc;
        IssuedReason = issuedReason;
    }

    public Guid Id { get; private set; }

    public Guid ShiftSlotId { get; private set; }

    public Guid VolunteerId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? InvalidatedAtUtc { get; private set; }

    public CapabilityIssuedReason IssuedReason { get; private set; }

    public static VolunteerAccessCapability Create(
        Guid shiftSlotId,
        Guid volunteerId,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc,
        CapabilityIssuedReason issuedReason) =>
        new(shiftSlotId, volunteerId, tokenHash, createdAtUtc, issuedReason);

    public bool IsActive => InvalidatedAtUtc is null;

    public bool IsUsable(
        DateTimeOffset nowUtc,
        DateTimeOffset shiftEndsAtUtc,
        bool slotIsActive,
        bool shiftIsActive,
        bool volunteerIsAnonymized)
    {
        ValidateUtc(nowUtc);
        ValidateUtc(shiftEndsAtUtc);
        return IsActive &&
               slotIsActive &&
               shiftIsActive &&
               !volunteerIsAnonymized &&
               nowUtc <= shiftEndsAtUtc.AddDays(7);
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
            throw new DomainException("Capability timestamps must be UTC.");
        }
    }
}
