namespace VolunteerCoordinator.Domain.Notifications;

public sealed class NotificationAttempt
{
    private NotificationAttempt()
    {
    }

    private NotificationAttempt(
        Guid transitionId,
        string kind,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new DomainException("Notification kind is required.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Notification timestamps must be UTC.");
        }

        Id = Guid.NewGuid();
        TransitionId = transitionId;
        Kind = kind.Trim();
        State = NotificationState.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TransitionId { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public NotificationState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? ErrorSummary { get; private set; }

    public static NotificationAttempt Create(
        Guid transitionId,
        string kind,
        DateTimeOffset createdAtUtc) =>
        new(transitionId, kind, createdAtUtc);

    // Compatibility overload for callers that already have a contact value.
    // The value is intentionally discarded and is never mapped or persisted.
    public static NotificationAttempt Create(
        Guid transitionId,
        string kind,
        string _,
        DateTimeOffset createdAtUtc) =>
        new(transitionId, kind, createdAtUtc);

    public void Succeed(DateTimeOffset nowUtc)
    {
        EnsurePending(nowUtc);
        State = NotificationState.Succeeded;
        CompletedAtUtc = nowUtc;
    }

    public void Fail(DateTimeOffset nowUtc, string safeErrorSummary)
    {
        EnsurePending(nowUtc);
        State = NotificationState.Failed;
        CompletedAtUtc = nowUtc;
        var error = string.IsNullOrWhiteSpace(safeErrorSummary)
            ? "Notification delivery is unavailable."
            : safeErrorSummary.Trim();
        ErrorSummary = error.Length <= 500 ? error : error[..500];
    }

    private void EnsurePending(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Notification timestamps must be UTC.");
        }

        if (State != NotificationState.Pending)
        {
            throw new DomainException("The notification attempt is already complete.");
        }
    }
}
