namespace VolunteerCoordinator.Domain.Notifications;

public sealed class NotificationIntent
{
    private NotificationIntent()
    {
    }

    private NotificationIntent(
        string eventKey,
        Guid transitionId,
        Guid volunteerId,
        Guid? shiftSlotId,
        string kind,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(eventKey) || eventKey.Trim().Length > 240)
        {
            throw new DomainException("A bounded notification event key is required.");
        }

        if (string.IsNullOrWhiteSpace(kind) || kind.Trim().Length > 100)
        {
            throw new DomainException("A bounded notification kind is required.");
        }

        ValidateUtc(createdAtUtc);
        Id = Guid.NewGuid();
        EventKey = eventKey.Trim();
        TransitionId = transitionId;
        VolunteerId = volunteerId;
        ShiftSlotId = shiftSlotId;
        Kind = kind.Trim();
        State = NotificationIntentState.Pending;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string EventKey { get; private set; } = string.Empty;

    public Guid TransitionId { get; private set; }

    public Guid VolunteerId { get; private set; }

    public Guid? ShiftSlotId { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public NotificationIntentState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? LeaseUntilUtc { get; private set; }

    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public DateTimeOffset? LastProviderEventAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public uint Version { get; private set; }

    public Guid? ClaimOwnerToken { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public string? FailureCategory { get; private set; }

    public Guid? RecoveryTokenId { get; private set; }

    public static NotificationIntent Create(
        string eventKey,
        Guid transitionId,
        Guid volunteerId,
        Guid? shiftSlotId,
        string kind,
        DateTimeOffset createdAtUtc) =>
        new(eventKey, transitionId, volunteerId, shiftSlotId, kind, createdAtUtc);

    public bool IsTerminal => State is NotificationIntentState.Delivered or
        NotificationIntentState.Bounced or
        NotificationIntentState.Complained or
        NotificationIntentState.Failed or
        NotificationIntentState.Cancelled;

    public bool IsClaimOwned(Guid claimOwnerToken) =>
        State == NotificationIntentState.InFlight &&
        ClaimOwnerToken == claimOwnerToken;

    public void SetRecoveryToken(Guid recoveryTokenId)
    {
        if (RecoveryTokenId == recoveryTokenId)
        {
            return;
        }

        RecoveryTokenId = recoveryTokenId;
        TouchVersion();
    }

    public void ClearRecoveryToken()
    {
        if (RecoveryTokenId is null)
        {
            return;
        }

        RecoveryTokenId = null;
        TouchVersion();
    }

    public Guid Claim(
        DateTimeOffset nowUtc,
        DateTimeOffset leaseUntilUtc,
        Guid? claimOwnerToken = null)
    {
        ValidateUtc(nowUtc);
        ValidateUtc(leaseUntilUtc);
        if (IsTerminal ||
            State == NotificationIntentState.Accepted ||
            AttemptCount >= 5)
        {
            throw new DomainException("This notification intent is no longer claimable.");
        }

        var ownerToken = claimOwnerToken.GetValueOrDefault();
        if (ownerToken == Guid.Empty)
        {
            ownerToken = Guid.NewGuid();
        }

        State = NotificationIntentState.InFlight;
        LeaseUntilUtc = leaseUntilUtc;
        ClaimOwnerToken = ownerToken;
        AttemptCount++;
        TouchVersion();
        return ownerToken;
    }

    public void Accept(DateTimeOffset nowUtc, string providerMessageId)
    {
        ValidateUtc(nowUtc);
        if (State is NotificationIntentState.Cancelled or
            NotificationIntentState.Bounced or
            NotificationIntentState.Complained or
            NotificationIntentState.Failed or
            NotificationIntentState.Delivered ||
            string.IsNullOrWhiteSpace(providerMessageId))
        {
            return;
        }

        State = NotificationIntentState.Accepted;
        AcceptedAtUtc = nowUtc;
        ProviderMessageId = providerMessageId.Trim()[..Math.Min(providerMessageId.Trim().Length, 200)];
        LeaseUntilUtc = null;
        ClaimOwnerToken = null;
        FailureCategory = null;
        TouchVersion();
    }

    public void ScheduleRetry(DateTimeOffset nowUtc, DateTimeOffset nextAttemptAtUtc, string category)
    {
        ValidateUtc(nowUtc);
        ValidateUtc(nextAttemptAtUtc);
        if (IsTerminal || State != NotificationIntentState.InFlight)
        {
            return;
        }

        LeaseUntilUtc = null;
        ClaimOwnerToken = null;
        FailureCategory = NormalizeCategory(category);
        if (AttemptCount >= 5)
        {
            State = NotificationIntentState.Failed;
            CompletedAtUtc = nowUtc;
            TouchVersion();
            return;
        }

        State = NotificationIntentState.RetryScheduled;
        NextAttemptAtUtc = nextAttemptAtUtc;
        TouchVersion();
    }

    public void Fail(DateTimeOffset nowUtc, string category)
    {
        ValidateUtc(nowUtc);
        if (IsTerminal)
        {
            return;
        }

        State = NotificationIntentState.Failed;
        CompletedAtUtc = nowUtc;
        LeaseUntilUtc = null;
        ClaimOwnerToken = null;
        FailureCategory = NormalizeCategory(category);
        TouchVersion();
    }

    public void Cancel(DateTimeOffset nowUtc, string category = "Cancelled")
    {
        ValidateUtc(nowUtc);
        if (IsTerminal)
        {
            return;
        }

        State = NotificationIntentState.Cancelled;
        CompletedAtUtc = nowUtc;
        LeaseUntilUtc = null;
        ClaimOwnerToken = null;
        FailureCategory = NormalizeCategory(category);
        TouchVersion();
    }

    public void ApplyWebhook(
        string eventType,
        DateTimeOffset providerOccurredAtUtc,
        DateTimeOffset processedAtUtc)
    {
        ValidateUtc(providerOccurredAtUtc);
        ValidateUtc(processedAtUtc);
        if (State is NotificationIntentState.Cancelled or
            NotificationIntentState.Failed or
            NotificationIntentState.Complained ||
            (LastProviderEventAtUtc is DateTimeOffset last &&
             providerOccurredAtUtc < last))
        {
            return;
        }

        switch (eventType)
        {
            case "email.delivered":
                if (State is NotificationIntentState.Bounced or NotificationIntentState.Delivered)
                {
                    return;
                }

                if (State is NotificationIntentState.Accepted or NotificationIntentState.InFlight)
                {
                    State = NotificationIntentState.Delivered;
                    DeliveredAtUtc = providerOccurredAtUtc;
                    LastProviderEventAtUtc = providerOccurredAtUtc;
                    CompletedAtUtc = processedAtUtc;
                    LeaseUntilUtc = null;
                    ClaimOwnerToken = null;
                    TouchVersion();
                }

                break;
            case "email.bounced":
                if (State is NotificationIntentState.Complained or NotificationIntentState.Bounced)
                {
                    return;
                }

                State = NotificationIntentState.Bounced;
                LastProviderEventAtUtc = providerOccurredAtUtc;
                CompletedAtUtc = processedAtUtc;
                LeaseUntilUtc = null;
                ClaimOwnerToken = null;
                FailureCategory = "Bounced";
                TouchVersion();
                break;
            case "email.complained":
                State = NotificationIntentState.Complained;
                LastProviderEventAtUtc = providerOccurredAtUtc;
                CompletedAtUtc = processedAtUtc;
                LeaseUntilUtc = null;
                ClaimOwnerToken = null;
                FailureCategory = "RecipientReportedSpam";
                TouchVersion();
                break;
        }
    }

    private static string NormalizeCategory(string value)
    {
        var category = string.IsNullOrWhiteSpace(value) ? "ProviderFailure" : value.Trim();
        return category.Length <= 100 ? category : category[..100];
    }

    private void TouchVersion() => Version++;

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Notification timestamps must be UTC.");
        }
    }
}
