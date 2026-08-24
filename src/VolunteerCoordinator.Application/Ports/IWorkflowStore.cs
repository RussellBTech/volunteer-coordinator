using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Application.Ports;

public interface IWorkflowStore
{
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Shift>> GetAllShiftsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Shift>> GetPublishedFutureShiftsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken cancellationToken);

    Task<ShiftSlot?> GetSlotAsync(Guid slotId, CancellationToken cancellationToken);
    Task LockSlotAsync(Guid slotId, CancellationToken cancellationToken);


    Task<Volunteer?> GetVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken);

    Task<Volunteer?> GetVolunteerByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Volunteer>> GetVolunteersAsync(CancellationToken cancellationToken);

    Task<ShiftRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken);

    Task<ShiftRequest?> GetRequestByStatusHashAsync(byte[] hash, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShiftRequest>> GetRequestsAsync(CancellationToken cancellationToken);

    Task<ShiftRequest?> GetPendingRequestAsync(
        Guid slotId,
        Guid volunteerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ShiftRequest>> GetPendingRequestsForSlotAsync(
        Guid slotId,
        CancellationToken cancellationToken);

    Task<Assignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken);

    Task<Assignment?> GetActiveAssignmentForSlotAsync(
        Guid slotId,
        CancellationToken cancellationToken);

    Task<Assignment?> GetActiveAssignmentForVolunteerAndShiftAsync(
        Guid volunteerId,
        Guid shiftId,
        CancellationToken cancellationToken);

    Task<Assignment?> GetAssignmentBySourceRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Assignment>> GetActiveAssignmentsAsync(
        IReadOnlyCollection<Guid> slotIds,
        CancellationToken cancellationToken);

    Task<ActionToken?> GetActionTokenByHashAsync(byte[] hash, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionToken>> GetUnusedActionTokensAsync(
        Guid assignmentId,
        VolunteerAction action,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(
        int limit,
        CancellationToken cancellationToken);

    void AddShift(Shift shift);
    void AddShiftSlots(IReadOnlyCollection<ShiftSlot> slots);


    void AddVolunteer(Volunteer volunteer);

    void AddRequest(ShiftRequest request);

    void AddAssignment(Assignment assignment);

    void AddActionToken(ActionToken actionToken);

    void AddAuditEntry(AuditEntry auditEntry);
}
