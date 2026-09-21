namespace VolunteerCoordinator.Domain.Schedules;

public sealed class RecurringShiftSeries
{
    private RecurringShiftSeries()
    {
    }

    private RecurringShiftSeries(DateTimeOffset createdAtUtc)
    {
        ValidateUtc(createdAtUtc, nameof(createdAtUtc));
        Id = Guid.NewGuid();
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public bool IsActive { get; private set; }

    public int CurrentRevisionNumber { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateOnly? LastGeneratedThroughLocalDate { get; private set; }

    public uint Version { get; private set; }

    public static RecurringShiftSeries Create(DateTimeOffset createdAtUtc) => new(createdAtUtc);

    public void AddRevision(RecurringShiftSeriesRevision revision, DateTimeOffset nowUtc)
    {
        if (revision.SeriesId != Id)
        {
            throw new DomainException("The revision belongs to another recurring series.");
        }

        ValidateUtc(nowUtc, nameof(nowUtc));
        if (revision.RevisionNumber != CurrentRevisionNumber + 1)
        {
            throw new DomainException("Recurring revisions must be added in order.");
        }

        CurrentRevisionNumber = revision.RevisionNumber;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkGeneratedThrough(DateOnly localDate, DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (LastGeneratedThroughLocalDate is null || localDate > LastGeneratedThroughLocalDate)
        {
            LastGeneratedThroughLocalDate = localDate;
        }

        UpdatedAtUtc = nowUtc;
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    private static void ValidateUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException($"{name} must be UTC.");
        }
    }
}
