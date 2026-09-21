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
    Task<IReadOnlyList<Shift>> GetPublishedCurrentOrFutureShiftsAsync(
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
    Task<IReadOnlyList<Volunteer>> GetVolunteersByIdsAsync(
        IReadOnlyCollection<Guid> volunteerIds,
        CancellationToken cancellationToken);

    Task<ShiftRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken);


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
    Task LockAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken);


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
    Task<CoordinatorHomeProjection> GetCoordinatorHomeProjectionAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CoordinatorHomeExample>> GetActionableMessageExamplesAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<CoordinatorMessagePageProjection> GetActionableMessagePageAsync(
        DateTimeOffset nowUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<CoordinatorWorkSummaryDto> GetCoordinatorWorkSummaryAsync(
        DateTimeOffset nowUtc,
        CoordinatorAttentionOptions options,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<CoordinatorWorkPageDto> GetCoordinatorWorkPageAsync(
        DateTimeOffset nowUtc,
        CoordinatorAttentionOptions options,
        CoordinatorWorkFilter filter,
        CoordinatorWorkCursor? cursor,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<VolunteerSearchResultDto>> SearchAssignableVolunteersAsync(
        string normalizedTerm,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<AuditHistoryQueryPage> GetAuditHistoryPageAsync(
        AuditHistoryFilter filter,
        AuditHistoryCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<string>> GetAuditActorsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<CoordinatorFilterChoiceDto>> SearchAuditShiftsAsync(
        string normalizedTerm,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    Task<IReadOnlyList<CoordinatorCoverageQueryRow>> GetCoordinatorCoveragePageAsync(
        DateTimeOffset nowUtc,
        string? attention,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<CoordinatorCoverageQueryRow?> GetCoordinatorCoverageSlotAsync(
        DateTimeOffset nowUtc,
        Guid slotId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<CoordinatorRequestQueryRow>> GetCoordinatorRequestPageAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<CoordinatorNotificationIntentQueryRow>> GetCoordinatorNotificationIntentPageAsync(
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<CoordinatorAccessVerificationDto>> GetCoordinatorAccessVerificationsAsync(
        IReadOnlyCollection<string> normalizedEmails,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    void AddShift(Shift shift);

    void AddGroupSettings(GroupSettings settings);
    void AddShiftSlots(IReadOnlyCollection<ShiftSlot> slots);

    void AddVolunteer(Volunteer volunteer);

    void AddRequest(ShiftRequest request);

    void AddAssignment(Assignment assignment);


    void AddAuditEntry(AuditEntry auditEntry);
}
