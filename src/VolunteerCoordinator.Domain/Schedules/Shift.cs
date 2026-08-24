namespace VolunteerCoordinator.Domain.Schedules;

public sealed class Shift
{
    private readonly List<ShiftSlot> _slots = [];

    private Shift()
    {
    }

    private Shift(
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        int backupSlotCount)
    {
        Validate(title, location, notes, startsAtUtc, endsAtUtc);
        if (backupSlotCount is < 0 or > 2)
        {
            throw new DomainException("A shift may have zero, one, or two backup slots.");
        }

        Id = Guid.NewGuid();
        Title = title.Trim();
        Location = NormalizeOptional(location);
        Notes = NormalizeOptional(notes);
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        IsActive = true;
        _slots.Add(new ShiftSlot(Id, SlotKind.Primary, 1));
        for (var position = 1; position <= backupSlotCount; position++)
        {
            _slots.Add(new ShiftSlot(Id, SlotKind.Backup, position));
        }
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Location { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyCollection<ShiftSlot> Slots => _slots;

    public static Shift Create(
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        int backupSlotCount) =>
        new(title, location, notes, startsAtUtc, endsAtUtc, backupSlotCount);

    public void Edit(
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (!IsActive)
        {
            throw new DomainException("An inactive shift cannot be edited.");
        }

        Validate(title, location, notes, startsAtUtc, endsAtUtc);
        if (PublishedAtUtc.HasValue && endsAtUtc <= nowUtc)
        {
            throw new DomainException("A published shift must end in the future.");
        }

        Title = title.Trim();
        Location = NormalizeOptional(location);
        Notes = NormalizeOptional(notes);
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public void ConfigureBackupSlots(int backupSlotCount)
    {
        if (backupSlotCount is < 0 or > 2)
        {
            throw new DomainException("A shift may have zero, one, or two backup slots.");
        }

        for (var position = 1; position <= 2; position++)
        {
            var slot = _slots.SingleOrDefault(x => x.Kind == SlotKind.Backup && x.Position == position);
            if (position <= backupSlotCount)
            {
                if (slot is null)
                {
                    _slots.Add(new ShiftSlot(Id, SlotKind.Backup, position));
                }
                else
                {
                    slot.Activate();
                }
            }
            else
            {
                slot?.Deactivate();
            }
        }
    }

    public void Publish(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (!IsActive)
        {
            throw new DomainException("An inactive shift cannot be published.");
        }

        if (EndsAtUtc <= nowUtc)
        {
            throw new DomainException("A shift must end in the future before it can be published.");
        }

        PublishedAtUtc ??= nowUtc;
    }

    public void Deactivate() => IsActive = false;

    private static void Validate(
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120)
        {
            throw new DomainException("Title is required and cannot exceed 120 characters.");
        }

        if (location?.Trim().Length > 200)
        {
            throw new DomainException("Location cannot exceed 200 characters.");
        }

        if (notes?.Trim().Length > 1000)
        {
            throw new DomainException("Notes cannot exceed 1000 characters.");
        }

        ValidateUtc(startsAtUtc, nameof(startsAtUtc));
        ValidateUtc(endsAtUtc, nameof(endsAtUtc));
        if (endsAtUtc <= startsAtUtc)
        {
            throw new DomainException("The shift end must be after its start.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException($"{name} must be UTC.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
