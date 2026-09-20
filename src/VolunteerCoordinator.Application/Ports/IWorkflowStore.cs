using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Application.Ports;

public interface IWorkflowStore
{
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Shift>> GetAllShiftsAsync(CancellationToken cancellationToken);

    Task<GroupSettings?> GetGroupSettingsAsync(CancellationToken cancellationToken);
    Task LockGroupSettingsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Shift>> GetPublishedFutureShiftsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken cancellationToken);
    Task LockShiftAsync(Guid shiftId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Shift>> GetShiftsForSlotIdsAsync(
        IReadOnlyCollection<Guid> slotIds,
        CancellationToken cancellationToken);

    Task<ShiftSlot?> GetSlotAsync(Guid slotId, CancellationToken cancellationToken);
    Task LockSlotAsync(Guid slotId, CancellationToken cancellationToken);
    Task<Volunteer?> GetVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken);
    Task LockVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken);
    Task<Guid?> GetVolunteerIdByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);
    Task<VolunteerRemovalLookupProjection?> GetVolunteerRemovalProjectionByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetRetentionCandidateIdsAsync(
        DateTimeOffset coarseCutoffUtc,
        Guid? afterVolunteerId,
        int batchSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ShiftRequest>> GetRequestsForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Assignment>> GetAssignmentsForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationAttempt>> GetNotificationAttemptsAsync(
        IReadOnlyCollection<Guid> transitionIds,
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

    Task<IReadOnlyList<ShiftRequest>> GetPendingRequestsAsync(
        IReadOnlyCollection<Guid> slotIds,
        CancellationToken cancellationToken);

    Task<Assignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken);

    Task<Guid?> GetAssignmentSlotIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken);

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

    Task<Guid?> GetActionTokenSlotIdAsync(
        byte[] hash,
        CancellationToken cancellationToken);

    Task<ActionToken?> GetActionTokenByHashAsync(byte[] hash, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionToken>> GetUnusedActionTokensAsync(
        Guid assignmentId,
        VolunteerAction action,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionToken>> GetUnusedActionTokensAsync(
        IReadOnlyCollection<Guid> assignmentIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(
        int limit,
        CancellationToken cancellationToken);

    void AddShift(Shift shift);

    void AddGroupSettings(GroupSettings settings);
    void AddShiftSlots(IReadOnlyCollection<ShiftSlot> slots);

    void AddVolunteer(Volunteer volunteer);

    void AddRequest(ShiftRequest request);

    void AddAssignment(Assignment assignment);

    void AddActionToken(ActionToken actionToken);

    void AddAuditEntry(AuditEntry auditEntry);
}
