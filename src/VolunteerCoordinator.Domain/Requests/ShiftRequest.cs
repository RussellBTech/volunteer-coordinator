namespace VolunteerCoordinator.Domain.Requests;

public sealed class ShiftRequest
{
    private ShiftRequest()
    {
    }

    private ShiftRequest(
        Guid shiftSlotId,
        Guid volunteerId,
        DateTimeOffset requestedAtUtc)
    {
        if (requestedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Request timestamps must be UTC.");
        }

        Id = Guid.NewGuid();
        ShiftSlotId = shiftSlotId;
        VolunteerId = volunteerId;
        Status = RequestStatus.Pending;
        RequestedAtUtc = requestedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ShiftSlotId { get; private set; }

    public Guid VolunteerId { get; private set; }

    public RequestStatus Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public string? ResolvedByCoordinatorEmail { get; private set; }


    public static ShiftRequest Create(
        Guid shiftSlotId,
        Guid volunteerId,
        DateTimeOffset requestedAtUtc) =>
        new(shiftSlotId, volunteerId, requestedAtUtc);

    public void Approve(string coordinatorEmail, DateTimeOffset nowUtc) =>
        Resolve(RequestStatus.Approved, coordinatorEmail, nowUtc);

    public void Reject(string coordinatorEmail, DateTimeOffset nowUtc) =>
        Resolve(RequestStatus.Rejected, coordinatorEmail, nowUtc);

    public void Supersede(string coordinatorEmail, DateTimeOffset nowUtc) =>
        Resolve(RequestStatus.Superseded, coordinatorEmail, nowUtc);


    private void Resolve(RequestStatus status, string coordinatorEmail, DateTimeOffset nowUtc)
    {
        if (Status != RequestStatus.Pending)
        {
            throw new DomainException("Only a pending request can be resolved.");
        }

        if (string.IsNullOrWhiteSpace(coordinatorEmail))
        {
            throw new DomainException("A coordinator identity is required.");
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Request timestamps must be UTC.");
        }

        Status = status;
        ResolvedAtUtc = nowUtc;
        ResolvedByCoordinatorEmail = coordinatorEmail.Trim().ToUpperInvariant();
    }
}
