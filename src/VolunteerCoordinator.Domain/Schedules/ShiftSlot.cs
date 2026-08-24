namespace VolunteerCoordinator.Domain.Schedules;

public sealed class ShiftSlot
{
    private ShiftSlot()
    {
    }

    internal ShiftSlot(Guid shiftId, SlotKind kind, int position)
    {
        if (position < 1 || (kind == SlotKind.Primary && position != 1) || (kind == SlotKind.Backup && position > 2))
        {
            throw new DomainException("The slot position is invalid.");
        }

        Id = Guid.NewGuid();
        ShiftId = shiftId;
        Kind = kind;
        Position = position;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid ShiftId { get; private set; }

    public SlotKind Kind { get; private set; }

    public int Position { get; private set; }

    public bool IsActive { get; private set; }

    internal void Deactivate() => IsActive = false;

    internal void Activate() => IsActive = true;
}
