using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;
using VolunteerCoordinator.Domain.Settings;
using Xunit;

namespace VolunteerCoordinator.UnitTests;

public sealed class VolunteerCoordinatorServiceTests
{
    [Fact]
    public async Task AssignmentLockRetryStopsAfterBoundedAttemptsAndReturnsConcurrencyConflict()
    {
        var now = DateTimeOffset.UtcNow;
        var shift = Shift.Create(
            "Retry coverage",
            null,
            null,
            now.AddDays(1),
            now.AddDays(1).AddHours(1),
            0);
        var slot = shift.Slots.Single();
        var volunteer = Volunteer.Create("Volunteer", "volunteer@example.org", null, now);
        var conflictingAssignment = Assignment.Create(
            Guid.NewGuid(),
            shift.Id,
            volunteer.Id,
            null,
            "coordinator@example.org",
            now);
        var store = new RestartingAssignmentLockStore(shift, slot, volunteer, conflictingAssignment);
        var service = new VolunteerCoordinatorService(
            store,
            new FixedClock(now),
            new FixedTokenService(),
            new SuccessfulNotificationService());

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            service.AssignDirectlyAsync(
                slot.Id,
                "Volunteer",
                "volunteer@example.org",
                null,
                "coordinator@example.org",
                default));

        Assert.Equal("The requested change conflicts with current schedule state. Reload and try again.", exception.Message);
        Assert.Equal(3, store.TransactionAttempts);
        Assert.Equal(3, store.RollbackCount);
        Assert.Equal(3, store.SlotLockAttempts);
    }

    [Theory]
    [InlineData(364, 1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(365, 0)]
    [InlineData(365, int.MaxValue)]
    public async Task RetentionSweepRejectsUnsafeBoundsBeforeQuery(
        int retentionDays,
        int batchSize)
    {
        var now = DateTimeOffset.UtcNow;
        var shift = Shift.Create(
            "Retention bounds",
            null,
            null,
            now.AddDays(1),
            now.AddDays(1).AddHours(1),
            0);
        var slot = shift.Slots.Single();
        var volunteer = Volunteer.Create("Volunteer", "volunteer@example.org", null, now);
        var store = new RestartingAssignmentLockStore(
            shift,
            slot,
            volunteer,
            Assignment.Create(
                Guid.NewGuid(),
                shift.Id,
                volunteer.Id,
                null,
                "coordinator@example.org",
                now));
        var service = new VolunteerCoordinatorService(
            store,
            new FixedClock(now),
            new FixedTokenService(),
            new SuccessfulNotificationService());

        await Assert.ThrowsAsync<DomainException>(() =>
            service.RunRetentionSweepAsync(retentionDays, batchSize, default));
        Assert.Equal(0, store.TransactionAttempts);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FixedTokenService : ITokenService
    {
        public GeneratedToken Generate() => new("unused", new byte[32]);

        public byte[] Hash(string rawToken) => new byte[32];

        public bool FixedTimeEquals(byte[] left, byte[] right) => left.SequenceEqual(right);
    }

    private sealed class SuccessfulNotificationService : INotificationService
    {
        public Task<NotificationResult> RecordAndSendAsync(NotificationMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationResult(true, null));
    }

    private sealed class RestartingAssignmentLockStore : IWorkflowStore
    {
        private readonly Shift _shift;
        private readonly ShiftSlot _slot;
        private readonly Volunteer _volunteer;
        private readonly Assignment _conflictingAssignment;
        private bool _slotLockHeld;

        public RestartingAssignmentLockStore(
            Shift shift,
            ShiftSlot slot,
            Volunteer volunteer,
            Assignment conflictingAssignment)
        {
            _shift = shift;
            _slot = slot;
            _volunteer = volunteer;
            _conflictingAssignment = conflictingAssignment;
        }

        public int TransactionAttempts { get; private set; }

        public int RollbackCount { get; private set; }

        public int SlotLockAttempts { get; private set; }

        public async Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            TransactionAttempts++;
            _slotLockHeld = false;
            try
            {
                return await operation(cancellationToken);
            }
            catch
            {
                RollbackCount++;
                throw;
            }
        }

        public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken cancellationToken) =>
            Task.FromResult<Shift?>(_shift);
        public Task LockShiftAsync(Guid shiftId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Shift>> GetShiftsForSlotIdsAsync(
            IReadOnlyCollection<Guid> slotIds,
            CancellationToken cancellationToken) =>
            Unsupported<IReadOnlyList<Shift>>();

        public Task<ShiftSlot?> GetSlotAsync(Guid slotId, CancellationToken cancellationToken) =>
            Task.FromResult<ShiftSlot?>(_slot);

        public Task LockSlotAsync(Guid slotId, CancellationToken cancellationToken)
        {
            SlotLockAttempts++;
            _slotLockHeld = true;
            return Task.CompletedTask;
        }
        public Task LockVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Guid?> GetVolunteerIdByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(_volunteer.Id);


        public Task<VolunteerRemovalLookupProjection?> GetVolunteerRemovalProjectionByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult<VolunteerRemovalLookupProjection?>(
                new VolunteerRemovalLookupProjection(_volunteer.Id, []));
        public Task<Assignment?> GetActiveAssignmentForSlotAsync(
            Guid slotId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Assignment?>(_slotLockHeld ? _conflictingAssignment : null);

        public Task<Assignment?> GetActiveAssignmentForVolunteerAndShiftAsync(
            Guid volunteerId,
            Guid shiftId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Assignment?>(null);

        public Task<T> Unsupported<T>() => throw new NotSupportedException();

        public Task FlushAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Shift>> GetAllShiftsAsync(CancellationToken cancellationToken) => Unsupported<IReadOnlyList<Shift>>();
        public Task<GroupSettings?> GetGroupSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<GroupSettings?>(GroupSettings.Create("Etc/UTC"));

        public Task LockGroupSettingsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<Shift>> GetPublishedFutureShiftsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<Shift>>();

        public Task<Volunteer?> GetVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken) =>
            Task.FromResult<Volunteer?>(_volunteer);

        public Task<IReadOnlyList<Guid>> GetRetentionCandidateIdsAsync(
            DateTimeOffset coarseCutoffUtc,
            Guid? afterVolunteerId,
            int batchSize,
            CancellationToken cancellationToken) =>
            Unsupported<IReadOnlyList<Guid>>();

        public Task<IReadOnlyList<ShiftRequest>> GetRequestsForVolunteerAsync(
            Guid volunteerId,
            CancellationToken cancellationToken) =>
            Unsupported<IReadOnlyList<ShiftRequest>>();

        public Task<IReadOnlyList<Assignment>> GetAssignmentsForVolunteerAsync(
            Guid volunteerId,
            CancellationToken cancellationToken) =>
            Unsupported<IReadOnlyList<Assignment>>();

        public Task<IReadOnlyList<NotificationAttempt>> GetNotificationAttemptsAsync(
            IReadOnlyCollection<Guid> transitionIds,
            CancellationToken cancellationToken) =>
            Unsupported<IReadOnlyList<NotificationAttempt>>();
        public Task<IReadOnlyList<Volunteer>> GetVolunteersAsync(CancellationToken cancellationToken) =>
            Unsupported<IReadOnlyList<Volunteer>>();


        public Task<ShiftRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken) => Unsupported<ShiftRequest?>();

        public Task<ShiftRequest?> GetRequestByStatusHashAsync(byte[] hash, CancellationToken cancellationToken) => Unsupported<ShiftRequest?>();

        public Task<IReadOnlyList<ShiftRequest>> GetRequestsAsync(CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ShiftRequest>>();

        public Task<ShiftRequest?> GetPendingRequestAsync(Guid slotId, Guid volunteerId, CancellationToken cancellationToken) => Unsupported<ShiftRequest?>();

        public Task<IReadOnlyList<ShiftRequest>> GetPendingRequestsForSlotAsync(Guid slotId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ShiftRequest>>();

        public Task<IReadOnlyList<ShiftRequest>> GetPendingRequestsAsync(IReadOnlyCollection<Guid> slotIds, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ShiftRequest>>();

        public Task<Assignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken) => Unsupported<Assignment?>();

        public Task<Guid?> GetAssignmentSlotIdAsync(Guid assignmentId, CancellationToken cancellationToken) => Unsupported<Guid?>();

        public Task<Assignment?> GetAssignmentBySourceRequestAsync(Guid requestId, CancellationToken cancellationToken) => Unsupported<Assignment?>();

        public Task<IReadOnlyList<Assignment>> GetActiveAssignmentsAsync(IReadOnlyCollection<Guid> slotIds, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<Assignment>>();

        public Task<Guid?> GetActionTokenSlotIdAsync(byte[] hash, CancellationToken cancellationToken) => Unsupported<Guid?>();

        public Task<ActionToken?> GetActionTokenByHashAsync(byte[] hash, CancellationToken cancellationToken) => Unsupported<ActionToken?>();

        public Task<IReadOnlyList<ActionToken>> GetUnusedActionTokensAsync(Guid assignmentId, VolunteerAction action, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ActionToken>>();

        public Task<IReadOnlyList<ActionToken>> GetUnusedActionTokensAsync(IReadOnlyCollection<Guid> assignmentIds, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ActionToken>>();

        public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<AuditEntry>>();

        public void AddShift(Shift shift) => throw new NotSupportedException();
        public void AddGroupSettings(GroupSettings settings) => throw new NotSupportedException();

        public void AddShiftSlots(IReadOnlyCollection<ShiftSlot> slots) => throw new NotSupportedException();

        public void AddVolunteer(Volunteer volunteer) => throw new NotSupportedException();

        public void AddRequest(ShiftRequest request) => throw new NotSupportedException();

        public void AddAssignment(Assignment assignment) => throw new NotSupportedException();

        public void AddActionToken(ActionToken actionToken) => throw new NotSupportedException();

        public void AddAuditEntry(AuditEntry auditEntry) => throw new NotSupportedException();
    }
}
