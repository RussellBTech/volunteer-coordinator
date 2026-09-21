namespace VolunteerCoordinator.Domain.Schedules;

public sealed class RecurringShiftOccurrence
{
    private RecurringShiftOccurrence()
    {
    }

    private RecurringShiftOccurrence(
        Guid seriesId,
        Guid revisionId,
        DateOnly localDate,
        RecurringOccurrenceStatus status,
        DateTimeOffset createdAtUtc)
    {
        if (seriesId == Guid.Empty || revisionId == Guid.Empty)
        {
            throw new DomainException("A recurring occurrence requires a series and revision.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Occurrence timestamps must be UTC.");
        }

        Id = Guid.NewGuid();
        SeriesId = seriesId;
        RevisionId = revisionId;
        LocalDate = localDate;
        Status = status;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid SeriesId { get; private set; }

    public Guid RevisionId { get; private set; }

    public DateOnly LocalDate { get; private set; }

    public Guid? ShiftId { get; private set; }

    public RecurringOccurrenceStatus Status { get; private set; }

    public bool IsException { get; private set; }

    public DateTime? ResolvedLocalStart { get; private set; }

    public string? ResolutionActor { get; private set; }

    public DateTimeOffset? ResolutionAtUtc { get; private set; }

    public string? ResolutionReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public uint Version { get; private set; }

    public static RecurringShiftOccurrence CreateNeedsReview(
        Guid seriesId,
        Guid revisionId,
        DateOnly localDate,
        DateTimeOffset createdAtUtc) =>
        new(seriesId, revisionId, localDate, RecurringOccurrenceStatus.NeedsReview, createdAtUtc);

    public static RecurringShiftOccurrence CreatePendingGenerated(
        Guid seriesId,
        Guid revisionId,
        DateOnly localDate,
        DateTimeOffset createdAtUtc) =>
        new(seriesId, revisionId, localDate, RecurringOccurrenceStatus.Generated, createdAtUtc);

    public void AttachShift(Guid shiftId)
    {
        if (Status != RecurringOccurrenceStatus.Generated)
        {
            throw new DomainException("Only a generated occurrence can reference a concrete shift.");
        }

        if (shiftId == Guid.Empty)
        {
            throw new DomainException("A concrete shift is required.");
        }

        if (ShiftId.HasValue && ShiftId != shiftId)
        {
            throw new DomainException("An occurrence can reference only one concrete shift.");
        }

        ShiftId = shiftId;
    }

    public void ApplyRevision(Guid revisionId)
    {
        if (revisionId == Guid.Empty)
        {
            throw new DomainException("A recurring revision is required.");
        }

        RevisionId = revisionId;
    }

    public void MarkException(string? reason = null)
    {
        IsException = true;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            ResolutionReason = reason.Trim();
        }
    }

    public void Resolve(
        TimeOnly replacementLocalStart,
        string coordinatorEmail,
        DateTimeOffset resolvedAtUtc,
        string? reason = null)
    {
        if (Status != RecurringOccurrenceStatus.NeedsReview)
        {
            throw new DomainException("Only a recurring occurrence needing review can be resolved.");
        }

        ValidateResolution(coordinatorEmail, resolvedAtUtc);
        Status = RecurringOccurrenceStatus.Generated;
        IsException = true;
        ResolvedLocalStart = DateTime.SpecifyKind(LocalDate.ToDateTime(replacementLocalStart), DateTimeKind.Unspecified);
        ResolutionActor = coordinatorEmail.Trim().ToUpperInvariant();
        ResolutionAtUtc = resolvedAtUtc;
        ResolutionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Skip(string reason, string coordinatorEmail, DateTimeOffset skippedAtUtc)
    {
        if (Status != RecurringOccurrenceStatus.NeedsReview)
        {
            throw new DomainException("Only a recurring occurrence needing review can be skipped.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required when a recurring occurrence is skipped.");
        }

        ValidateResolution(coordinatorEmail, skippedAtUtc);
        Status = RecurringOccurrenceStatus.Skipped;
        IsException = true;
        ResolutionActor = coordinatorEmail.Trim().ToUpperInvariant();
        ResolutionAtUtc = skippedAtUtc;
        ResolutionReason = reason.Trim();
    }

    private static void ValidateResolution(string coordinatorEmail, DateTimeOffset atUtc)
    {
        if (string.IsNullOrWhiteSpace(coordinatorEmail))
        {
            throw new DomainException("A coordinator identity is required.");
        }

        if (atUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Occurrence resolution timestamps must be UTC.");
        }
    }
}
