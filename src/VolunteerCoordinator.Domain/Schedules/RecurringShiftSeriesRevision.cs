namespace VolunteerCoordinator.Domain.Schedules;

public sealed class RecurringShiftSeriesRevision
{
    private RecurringShiftSeriesRevision()
    {
    }

    private RecurringShiftSeriesRevision(
        Guid seriesId,
        int revisionNumber,
        DateOnly effectiveLocalDate,
        string title,
        string? location,
        string? volunteerInstructions,
        string? internalCoordinatorNotes,
        RecurrenceKind recurrenceKind,
        int interval,
        DayOfWeekMask weeklyDays,
        DateOnly anchorLocalDate,
        TimeOnly localStartTime,
        int durationMinutes,
        int backupSlotCount,
        int horizonWeeks,
        string timeZoneId,
        AmbiguousTimeChoice ambiguousTimeChoice,
        string createdByCoordinator,
        DateTimeOffset createdAtUtc)
    {
        ValidateText(title, location, volunteerInstructions, internalCoordinatorNotes);
        if (seriesId == Guid.Empty)
        {
            throw new DomainException("A recurring series is required.");
        }

        if (revisionNumber < 1)
        {
            throw new DomainException("A recurring revision number must be positive.");
        }

        if (interval is < 1 or > 4)
        {
            throw new DomainException("A recurrence interval must be between one and four.");
        }

        if (recurrenceKind == RecurrenceKind.Weekly && weeklyDays == DayOfWeekMask.None)
        {
            throw new DomainException("Choose at least one weekday for a weekly series.");
        }

        if (recurrenceKind == RecurrenceKind.Daily && weeklyDays != DayOfWeekMask.None)
        {
            throw new DomainException("A daily series cannot include selected weekdays.");
        }

        if (durationMinutes is < 1 or > 10080)
        {
            throw new DomainException("Elapsed duration must be between one minute and seven days.");
        }

        if (backupSlotCount is < 0 or > 2)
        {
            throw new DomainException("A recurring shift may have zero, one, or two backup slots.");
        }

        if (horizonWeeks is < 4 or > 26)
        {
            throw new DomainException("A recurring horizon must be between four and twenty-six weeks.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Trim().Length > 200)
        {
            throw new DomainException("An IANA time-zone snapshot is required.");
        }

        if (string.IsNullOrWhiteSpace(createdByCoordinator))
        {
            throw new DomainException("A coordinator identity is required.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Revision timestamps must be UTC.");
        }

        Id = Guid.NewGuid();
        SeriesId = seriesId;
        RevisionNumber = revisionNumber;
        EffectiveLocalDate = effectiveLocalDate;
        Title = title.Trim();
        Location = NormalizeOptional(location);
        VolunteerInstructions = NormalizeOptional(volunteerInstructions);
        InternalCoordinatorNotes = NormalizeOptional(internalCoordinatorNotes);
        RecurrenceKind = recurrenceKind;
        Interval = interval;
        WeeklyDays = weeklyDays;
        AnchorLocalDate = anchorLocalDate;
        LocalStartTime = localStartTime;
        DurationMinutes = durationMinutes;
        BackupSlotCount = backupSlotCount;
        HorizonWeeks = horizonWeeks;
        TimeZoneId = timeZoneId.Trim();
        AmbiguousTimeChoice = ambiguousTimeChoice;
        CreatedByCoordinator = createdByCoordinator.Trim().ToUpperInvariant();
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid SeriesId { get; private set; }

    public int RevisionNumber { get; private set; }

    public DateOnly EffectiveLocalDate { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Location { get; private set; }

    public string? VolunteerInstructions { get; private set; }

    public string? InternalCoordinatorNotes { get; private set; }

    public RecurrenceKind RecurrenceKind { get; private set; }

    public int Interval { get; private set; }

    public DayOfWeekMask WeeklyDays { get; private set; }

    public DateOnly AnchorLocalDate { get; private set; }

    public TimeOnly LocalStartTime { get; private set; }

    public int DurationMinutes { get; private set; }

    public int BackupSlotCount { get; private set; }

    public int HorizonWeeks { get; private set; }

    public string TimeZoneId { get; private set; } = string.Empty;

    public AmbiguousTimeChoice AmbiguousTimeChoice { get; private set; }

    public string CreatedByCoordinator { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static RecurringShiftSeriesRevision Create(
        Guid seriesId,
        int revisionNumber,
        DateOnly effectiveLocalDate,
        string title,
        string? location,
        string? volunteerInstructions,
        string? internalCoordinatorNotes,
        RecurrenceKind recurrenceKind,
        int interval,
        DayOfWeekMask weeklyDays,
        DateOnly anchorLocalDate,
        TimeOnly localStartTime,
        int durationMinutes,
        int backupSlotCount,
        int horizonWeeks,
        string timeZoneId,
        AmbiguousTimeChoice ambiguousTimeChoice,
        string createdByCoordinator,
        DateTimeOffset createdAtUtc) => new(
            seriesId,
            revisionNumber,
            effectiveLocalDate,
            title,
            location,
            volunteerInstructions,
            internalCoordinatorNotes,
            recurrenceKind,
            interval,
            weeklyDays,
            anchorLocalDate,
            localStartTime,
            durationMinutes,
            backupSlotCount,
            horizonWeeks,
            timeZoneId,
            ambiguousTimeChoice,
            createdByCoordinator,
            createdAtUtc);

    public bool IncludesDate(DateOnly localDate)
    {
        if (localDate < AnchorLocalDate)
        {
            return false;
        }

        if (RecurrenceKind == RecurrenceKind.Daily)
        {
            return localDate.DayNumberDifference(AnchorLocalDate) % Interval == 0;
        }

        var monday = StartOfIsoWeek(AnchorLocalDate);
        var localWeek = StartOfIsoWeek(localDate);
        var weeks = localWeek.DayNumberDifference(monday) / 7;
        return weeks >= 0 && weeks % Interval == 0 && WeeklyDays.Contains(localDate.DayOfWeek);
    }

    private static void ValidateText(string title, string? location, string? instructions, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120)
        {
            throw new DomainException("Title is required and cannot exceed 120 characters.");
        }

        if (location?.Trim().Length > 200)
        {
            throw new DomainException("Location cannot exceed 200 characters.");
        }

        if (instructions?.Trim().Length > 1000)
        {
            throw new DomainException("Volunteer instructions cannot exceed 1000 characters.");
        }

        if (notes?.Trim().Length > 1000)
        {
            throw new DomainException("Notes cannot exceed 1000 characters.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly StartOfIsoWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}

public static class DayOfWeekMaskExtensions
{
    public static bool Contains(this DayOfWeekMask mask, DayOfWeek day) =>
        (mask & day.ToMask()) != DayOfWeekMask.None;

    public static DayOfWeekMask ToMask(this DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => DayOfWeekMask.Sunday,
        DayOfWeek.Monday => DayOfWeekMask.Monday,
        DayOfWeek.Tuesday => DayOfWeekMask.Tuesday,
        DayOfWeek.Wednesday => DayOfWeekMask.Wednesday,
        DayOfWeek.Thursday => DayOfWeekMask.Thursday,
        DayOfWeek.Friday => DayOfWeekMask.Friday,
        DayOfWeek.Saturday => DayOfWeekMask.Saturday,
        _ => DayOfWeekMask.None
    };

    public static int DayNumberDifference(this DateOnly value, DateOnly other) => value.DayNumber - other.DayNumber;
}
