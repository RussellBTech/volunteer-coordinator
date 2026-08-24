namespace VolunteerCoordinator.Domain.Assignments;

public sealed class Assignment
{
    private Assignment()
    {
    }

    private Assignment(
        Guid shiftSlotId,
        Guid shiftId,
        Guid volunteerId,
        Guid? sourceRequestId,
        string coordinatorEmail,
        DateTimeOffset assignedAtUtc)
    {
        ValidateCoordinator(coordinatorEmail);
        ValidateUtc(assignedAtUtc);
        Id = Guid.NewGuid();
        ShiftSlotId = shiftSlotId;
        ShiftId = shiftId;
        VolunteerId = volunteerId;
        SourceRequestId = sourceRequestId;
        Status = AssignmentStatus.Assigned;
        AssignedAtUtc = assignedAtUtc;
        AssignedByCoordinatorEmail = coordinatorEmail.Trim().ToUpperInvariant();
    }

    public Guid Id { get; private set; }

    public Guid ShiftSlotId { get; private set; }

    public Guid ShiftId { get; private set; }

    public Guid VolunteerId { get; private set; }

    public Guid? SourceRequestId { get; private set; }

    public AssignmentStatus Status { get; private set; }

    public DateTimeOffset AssignedAtUtc { get; private set; }

    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public string AssignedByCoordinatorEmail { get; private set; } = string.Empty;

    public bool IsActive => Status is AssignmentStatus.Assigned or AssignmentStatus.Confirmed;

    public static Assignment Create(
        Guid shiftSlotId,
        Guid shiftId,
        Guid volunteerId,
        Guid? sourceRequestId,
        string coordinatorEmail,
        DateTimeOffset assignedAtUtc) =>
        new(shiftSlotId, shiftId, volunteerId, sourceRequestId, coordinatorEmail, assignedAtUtc);

    public void Confirm(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (Status != AssignmentStatus.Assigned)
        {
            throw new DomainException("Only an assigned slot can be confirmed.");
        }

        Status = AssignmentStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
    }

    public void Decline(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (Status != AssignmentStatus.Assigned)
        {
            throw new DomainException("Only an unconfirmed assignment can be declined.");
        }

        End(AssignmentStatus.Declined, nowUtc);
    }

    public void Cancel(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (!IsActive)
        {
            throw new DomainException("Only an active assignment can be cancelled.");
        }

        End(AssignmentStatus.Cancelled, nowUtc);
    }

    public void Reassign(DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc);
        if (!IsActive)
        {
            throw new DomainException("Only an active assignment can be reassigned.");
        }

        End(AssignmentStatus.Reassigned, nowUtc);
    }

    private void End(AssignmentStatus status, DateTimeOffset nowUtc)
    {
        Status = status;
        EndedAtUtc = nowUtc;
    }

    private static void ValidateCoordinator(string coordinatorEmail)
    {
        if (string.IsNullOrWhiteSpace(coordinatorEmail))
        {
            throw new DomainException("A coordinator identity is required.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Assignment timestamps must be UTC.");
        }
    }
}
