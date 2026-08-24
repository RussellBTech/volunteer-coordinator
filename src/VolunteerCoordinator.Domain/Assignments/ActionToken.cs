namespace VolunteerCoordinator.Domain.Assignments;

public sealed class ActionToken
{
    private ActionToken()
    {
    }

    private ActionToken(
        Guid assignmentId,
        VolunteerAction action,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (tokenHash.Length != 32)
        {
            throw new DomainException("A SHA-256 action-token hash is required.");
        }

        ValidateUtc(createdAtUtc);
        ValidateUtc(expiresAtUtc);
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new DomainException("The action token must expire after it is created.");
        }

        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        Action = action;
        TokenHash = tokenHash.ToArray();
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid AssignmentId { get; private set; }

    public VolunteerAction Action { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public static ActionToken Create(
        Guid assignmentId,
        VolunteerAction action,
        byte[] tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new(assignmentId, action, tokenHash, createdAtUtc, expiresAtUtc);

    public bool IsUsable(DateTimeOffset nowUtc) => UsedAtUtc is null && nowUtc <= ExpiresAtUtc;

    public void Consume(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (UsedAtUtc.HasValue)
        {
            throw new DomainException("This action link has already been used.");
        }

        if (nowUtc > ExpiresAtUtc)
        {
            throw new DomainException("This action link has expired.");
        }

        UsedAtUtc = nowUtc;
    }

    public void Invalidate(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        UsedAtUtc ??= nowUtc;
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Action-token timestamps must be UTC.");
        }
    }
}
