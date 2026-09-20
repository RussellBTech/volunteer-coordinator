using Microsoft.EntityFrameworkCore;
using Npgsql;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Infrastructure.Persistence;

public sealed class EfWorkflowStore : IWorkflowStore
{
    private const int RemovalLookupFetchLimit = 20;
    private const int RemovalLookupResultLimit = 10;
    private readonly VolunteerCoordinatorDbContext _dbContext;
    private sealed class HomeExampleRow
    {
        public Guid ShiftId { get; init; }
        public Guid? SlotId { get; init; }
        public string ShiftTitle { get; init; } = string.Empty;
        public string SlotLabel { get; init; } = string.Empty;
        public DateTimeOffset StartsAtUtc { get; init; }
        public DateTimeOffset EndsAtUtc { get; init; }
        public string? VolunteerName { get; init; }
        public DateTimeOffset? OccurredAtUtc { get; init; }
        public string? MessageKind { get; init; }
        public string? VolunteerEmail { get; init; }
        public string? VolunteerPhone { get; init; }
    }

    public EfWorkflowStore(VolunteerCoordinatorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw new DomainException("The record changed while it was being saved. Reload and try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw new DomainException("The requested change conflicts with current schedule state. Reload and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<Shift>> GetAllShiftsAsync(CancellationToken cancellationToken) =>
        await _dbContext.Shifts.Include(x => x.Slots).ToListAsync(cancellationToken);

    public async Task<GroupSettings?> GetGroupSettingsAsync(CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<GroupSettings>()
            .SingleOrDefault(x => x.Entity.Id == GroupSettings.SingletonId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.GroupSettings
            .SingleOrDefaultAsync(x => x.Id == GroupSettings.SingletonId, cancellationToken);
    }

    public Task LockGroupSettingsAsync(CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "GroupSettings" WHERE "Id" = {GroupSettings.SingletonId} FOR UPDATE""",
            cancellationToken);


    public async Task<IReadOnlyList<Shift>> GetPublishedFutureShiftsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        await _dbContext.Shifts
            .Include(x => x.Slots)
            .Where(x => x.IsActive && x.PublishedAtUtc != null && x.StartsAtUtc > nowUtc)
            .OrderBy(x => x.StartsAtUtc)
            .ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<Shift>> GetPublishedCurrentOrFutureShiftsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        await _dbContext.Shifts
            .Include(x => x.Slots)
            .Where(x => x.IsActive && x.PublishedAtUtc != null && x.EndsAtUtc > nowUtc)
            .OrderBy(x => x.StartsAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<Shift>()
            .SingleOrDefault(x => x.Entity.Id == shiftId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.Shifts.Include(x => x.Slots).SingleOrDefaultAsync(x => x.Id == shiftId, cancellationToken);
    }
    public Task LockShiftAsync(Guid shiftId, CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Shifts" WHERE "Id" = {shiftId} FOR UPDATE""",
            cancellationToken);

    public async Task<IReadOnlyList<Shift>> GetShiftsForSlotIdsAsync(
        IReadOnlyCollection<Guid> slotIds,
        CancellationToken cancellationToken)
    {
        if (slotIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Shifts
            .AsNoTracking()
            .Include(x => x.Slots)
            .Where(x => x.Slots.Any(slot => slotIds.Contains(slot.Id)))
            .ToListAsync(cancellationToken);
    }


    public async Task<ShiftSlot?> GetSlotAsync(Guid slotId, CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<ShiftSlot>()
            .SingleOrDefault(x => x.Entity.Id == slotId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.ShiftSlots.SingleOrDefaultAsync(x => x.Id == slotId, cancellationToken);
    }

    public async Task<ShiftRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<ShiftRequest>()
            .SingleOrDefault(x => x.Entity.Id == requestId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.ShiftRequests.SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    }

    public async Task LockSlotAsync(Guid slotId, CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "ShiftSlots" WHERE "Id" = {slotId} FOR UPDATE""",
            cancellationToken);
    }

    public async Task<Volunteer?> GetVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<Volunteer>()
            .SingleOrDefault(x => x.Entity.Id == volunteerId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.Volunteers.SingleOrDefaultAsync(x => x.Id == volunteerId, cancellationToken);
    }
    public Task<Guid?> GetVolunteerIdByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        _dbContext.Volunteers
            .AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);


    public async Task<VolunteerRemovalLookupProjection?> GetVolunteerRemovalProjectionByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var volunteerId = await _dbContext.Volunteers
            .AsNoTracking()
            .Where(x => x.AnonymizedAtUtc == null && x.NormalizedEmail == normalizedEmail)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!volunteerId.HasValue)
        {
            return null;
        }

        var requestCommitments = await (
            from request in _dbContext.ShiftRequests.AsNoTracking()
            join slot in _dbContext.ShiftSlots.AsNoTracking() on request.ShiftSlotId equals slot.Id
            join shift in _dbContext.Shifts.AsNoTracking() on slot.ShiftId equals shift.Id
            where request.VolunteerId == volunteerId.Value
            orderby shift.EndsAtUtc descending, request.RequestedAtUtc descending, request.Id
            select new VolunteerRemovalCommitmentProjection(
                shift.Id,
                slot.Id,
                shift.Title,
                shift.StartsAtUtc,
                shift.EndsAtUtc,
                shift.Location,
                shift.VolunteerInstructions,
                slot.Kind,
                slot.Position,
                request.Status,
                null))
            .Take(RemovalLookupFetchLimit)
            .ToListAsync(cancellationToken);

        var assignmentCommitments = await (
            from assignment in _dbContext.Assignments.AsNoTracking()
            join slot in _dbContext.ShiftSlots.AsNoTracking() on assignment.ShiftSlotId equals slot.Id
            join shift in _dbContext.Shifts.AsNoTracking() on assignment.ShiftId equals shift.Id
            where assignment.VolunteerId == volunteerId.Value
            orderby shift.EndsAtUtc descending, assignment.AssignedAtUtc descending, assignment.Id
            select new VolunteerRemovalCommitmentProjection(
                shift.Id,
                slot.Id,
                shift.Title,
                shift.StartsAtUtc,
                shift.EndsAtUtc,
                shift.Location,
                shift.VolunteerInstructions,
                slot.Kind,
                slot.Position,
                null,
                assignment.Status))
            .Take(RemovalLookupFetchLimit)
            .ToListAsync(cancellationToken);

        var commitments = requestCommitments
            .Concat(assignmentCommitments)
            .GroupBy(x => (x.ShiftId, x.SlotId))
            .Select(group => group
                .OrderByDescending(x => x.AssignmentStatus.HasValue)
                .ThenByDescending(x => x.EndsAtUtc)
                .First())
            .OrderByDescending(x => x.EndsAtUtc)
            .ThenByDescending(x => x.ShiftId)
            .Take(RemovalLookupResultLimit)
            .ToArray();

        return new VolunteerRemovalLookupProjection(volunteerId.Value, commitments);
    }

    public Task LockVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Volunteers" WHERE "Id" = {volunteerId} FOR UPDATE""",
            cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetRetentionCandidateIdsAsync(
        DateTimeOffset coarseCutoffUtc,
        Guid? afterVolunteerId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < VolunteerRetentionPolicy.MinimumBatchSize or > VolunteerRetentionPolicy.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
        var query = _dbContext.Volunteers
            .AsNoTracking()
            .Where(x => x.AnonymizedAtUtc == null && x.UpdatedAtUtc <= coarseCutoffUtc);
        if (afterVolunteerId.HasValue)
        {
            query = query.Where(x => x.Id > afterVolunteerId.Value);
        }

        return await query
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftRequest>> GetRequestsForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken) =>
        await _dbContext.ShiftRequests
            .Where(x => x.VolunteerId == volunteerId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Assignment>> GetAssignmentsForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken) =>
        await _dbContext.Assignments
            .Where(x => x.VolunteerId == volunteerId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NotificationAttempt>> GetNotificationAttemptsAsync(
        IReadOnlyCollection<Guid> transitionIds,
        CancellationToken cancellationToken)
    {
        if (transitionIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.NotificationAttempts
            .Where(x => transitionIds.Contains(x.TransitionId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Volunteer>> GetVolunteersAsync(CancellationToken cancellationToken) =>
        await _dbContext.Volunteers.ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<Volunteer>> GetVolunteersByIdsAsync(
        IReadOnlyCollection<Guid> volunteerIds,
        CancellationToken cancellationToken)
    {
        if (volunteerIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Volunteers
            .Where(x => volunteerIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
    }


    public Task<ShiftRequest?> GetRequestByStatusHashAsync(byte[] hash, CancellationToken cancellationToken) =>
        _dbContext.ShiftRequests.SingleOrDefaultAsync(x => x.StatusTokenHash.SequenceEqual(hash), cancellationToken);

    public async Task<IReadOnlyList<ShiftRequest>> GetRequestsAsync(CancellationToken cancellationToken) =>
        await _dbContext.ShiftRequests.OrderByDescending(x => x.RequestedAtUtc).ToListAsync(cancellationToken);

    public Task<ShiftRequest?> GetPendingRequestAsync(
        Guid slotId,
        Guid volunteerId,
        CancellationToken cancellationToken) =>
        _dbContext.ShiftRequests.SingleOrDefaultAsync(
            x => x.ShiftSlotId == slotId && x.VolunteerId == volunteerId && x.Status == RequestStatus.Pending,
            cancellationToken);

    public async Task<IReadOnlyList<ShiftRequest>> GetPendingRequestsForSlotAsync(
        Guid slotId,
        CancellationToken cancellationToken) =>
        await _dbContext.ShiftRequests
            .Where(x => x.ShiftSlotId == slotId && x.Status == RequestStatus.Pending)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ShiftRequest>> GetPendingRequestsAsync(
        IReadOnlyCollection<Guid> slotIds,
        CancellationToken cancellationToken)
    {
        if (slotIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.ShiftRequests
            .Where(x => slotIds.Contains(x.ShiftSlotId) && x.Status == RequestStatus.Pending)
            .ToListAsync(cancellationToken);
    }

    public async Task<Assignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<Assignment>()
            .SingleOrDefault(x => x.Entity.Id == assignmentId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.Assignments.SingleOrDefaultAsync(x => x.Id == assignmentId, cancellationToken);
    }

    public Task<Guid?> GetAssignmentSlotIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken) =>
        _dbContext.Assignments
            .Where(x => x.Id == assignmentId)
            .Select(x => (Guid?)x.ShiftSlotId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Assignment?> GetActiveAssignmentForSlotAsync(
        Guid slotId,
        CancellationToken cancellationToken)
    {
        var assignmentId = await _dbContext.Assignments
            .AsNoTracking()
            .Where(x => x.ShiftSlotId == slotId &&
                        (x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed))
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return assignmentId.HasValue
            ? await GetAssignmentAsync(assignmentId.Value, cancellationToken)
            : null;
    }

    public async Task<Assignment?> GetActiveAssignmentForVolunteerAndShiftAsync(
        Guid volunteerId,
        Guid shiftId,
        CancellationToken cancellationToken)
    {
        var assignmentId = await _dbContext.Assignments
            .AsNoTracking()
            .Where(x => x.VolunteerId == volunteerId &&
                        x.ShiftId == shiftId &&
                        (x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed))
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return assignmentId.HasValue
            ? await GetAssignmentAsync(assignmentId.Value, cancellationToken)
            : null;
    }

    public Task<Assignment?> GetAssignmentBySourceRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken) =>
        _dbContext.Assignments.SingleOrDefaultAsync(
            x => x.SourceRequestId == requestId,
            cancellationToken);

    public async Task<IReadOnlyList<Assignment>> GetActiveAssignmentsAsync(
        IReadOnlyCollection<Guid> slotIds,
        CancellationToken cancellationToken)
    {
        if (slotIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Assignments
            .Where(x => slotIds.Contains(x.ShiftSlotId) &&
                        (x.Status == AssignmentStatus.Assigned || x.Status == AssignmentStatus.Confirmed))
            .ToListAsync(cancellationToken);
    }

    public Task<Guid?> GetActionTokenSlotIdAsync(
        byte[] hash,
        CancellationToken cancellationToken) =>
        _dbContext.ActionTokens
            .Where(x => x.TokenHash.SequenceEqual(hash))
            .Join(
                _dbContext.Assignments,
                actionToken => actionToken.AssignmentId,
                assignment => assignment.Id,
                (_, assignment) => (Guid?)assignment.ShiftSlotId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ActionToken?> GetActionTokenByHashAsync(byte[] hash, CancellationToken cancellationToken)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<ActionToken>()
            .SingleOrDefault(x => x.Entity.TokenHash.SequenceEqual(hash));
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.State == EntityState.Detached ? null : trackedEntry.Entity;
        }

        return await _dbContext.ActionTokens
            .SingleOrDefaultAsync(x => x.TokenHash.SequenceEqual(hash), cancellationToken);
    }

    public async Task<IReadOnlyList<ActionToken>> GetUnusedActionTokensAsync(
        Guid assignmentId,
        VolunteerAction action,
        CancellationToken cancellationToken) =>
        await _dbContext.ActionTokens
            .Where(x => x.AssignmentId == assignmentId && x.Action == action && x.UsedAtUtc == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ActionToken>> GetUnusedActionTokensAsync(
        IReadOnlyCollection<Guid> assignmentIds,
        CancellationToken cancellationToken)
    {
        if (assignmentIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.ActionTokens
            .Where(x => assignmentIds.Contains(x.AssignmentId) && x.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await _dbContext.AuditEntries
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    public async Task<CoordinatorHomeProjection> GetCoordinatorHomeProjectionAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var settings = await _dbContext.GroupSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        var hasPublishedShift = await _dbContext.Shifts
            .AsNoTracking()
            .AnyAsync(x => x.PublishedAtUtc.HasValue, cancellationToken);
        var firstUnpublishedShift = await _dbContext.Shifts
            .AsNoTracking()
            .Include(x => x.Slots)
            .Where(x => x.IsActive && !x.PublishedAtUtc.HasValue && x.EndsAtUtc > nowUtc)
            .OrderBy(x => x.StartsAtUtc)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var firstExpiredUnpublishedShift = await _dbContext.Shifts
            .AsNoTracking()
            .Include(x => x.Slots)
            .Where(x => x.IsActive && !x.PublishedAtUtc.HasValue && x.EndsAtUtc <= nowUtc)
            .OrderByDescending(x => x.StartsAtUtc)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var pendingRequests = from request in _dbContext.ShiftRequests.AsNoTracking()
                              join slot in _dbContext.ShiftSlots.AsNoTracking()
                                  on request.ShiftSlotId equals slot.Id
                              join shift in _dbContext.Shifts.AsNoTracking()
                                  on slot.ShiftId equals shift.Id
                              join volunteer in _dbContext.Volunteers.AsNoTracking()
                                  on request.VolunteerId equals volunteer.Id
                              where request.Status == RequestStatus.Pending
                                  && slot.IsActive
                                  && shift.IsActive
                                  && shift.EndsAtUtc > nowUtc
                              select new HomeExampleRow
                              {
                                  ShiftId = shift.Id,
                                  SlotId = slot.Id,
                                  ShiftTitle = shift.Title,
                                  SlotLabel = slot.Kind == SlotKind.Primary ? "Primary" : "Backup " + slot.Position,
                                  StartsAtUtc = shift.StartsAtUtc,
                                  EndsAtUtc = shift.EndsAtUtc,
                                  VolunteerName = volunteer.AnonymizedAtUtc.HasValue ? "Removed volunteer" : volunteer.Name,
                                  OccurredAtUtc = request.RequestedAtUtc
                              };
        var pendingRequestCount = await pendingRequests.CountAsync(cancellationToken);
        var pendingRequestExamples = (await pendingRequests
                .OrderBy(x => x.StartsAtUtc)
                .ThenByDescending(x => x.OccurredAtUtc)
                .ThenBy(x => x.ShiftId)
                .ThenBy(x => x.SlotId)
                .Take(3)
                .ToListAsync(cancellationToken))
            .Select(ToHomeExample)
            .ToArray();

        var uncoveredCommitments = from slot in _dbContext.ShiftSlots.AsNoTracking()
                                   join shift in _dbContext.Shifts.AsNoTracking()
                                       on slot.ShiftId equals shift.Id
                                   where slot.IsActive
                                       && shift.IsActive
                                       && shift.PublishedAtUtc.HasValue
                                       && shift.EndsAtUtc > nowUtc
                                       && !_dbContext.Assignments.Any(assignment =>
                                           assignment.ShiftSlotId == slot.Id
                                           && (assignment.Status == AssignmentStatus.Assigned
                                               || assignment.Status == AssignmentStatus.Confirmed))
                                   select new HomeExampleRow
                                   {
                                       ShiftId = shift.Id,
                                       SlotId = slot.Id,
                                       ShiftTitle = shift.Title,
                                       SlotLabel = slot.Kind == SlotKind.Primary ? "Primary" : "Backup " + slot.Position,
                                       StartsAtUtc = shift.StartsAtUtc,
                                       EndsAtUtc = shift.EndsAtUtc
                                   };

        var uncoveredCommitmentCount = await uncoveredCommitments.CountAsync(cancellationToken);
        var uncoveredCommitmentExamples = (await uncoveredCommitments
                .OrderBy(x => x.StartsAtUtc)
                .ThenBy(x => x.ShiftId)
                .ThenBy(x => x.SlotId)
                .Take(3)
                .ToListAsync(cancellationToken))
            .Select(ToHomeExample)
            .ToArray();

        var unconfirmedAssignments = from assignment in _dbContext.Assignments.AsNoTracking()
                                     join slot in _dbContext.ShiftSlots.AsNoTracking()
                                         on assignment.ShiftSlotId equals slot.Id
                                     join shift in _dbContext.Shifts.AsNoTracking()
                                         on assignment.ShiftId equals shift.Id
                                     join volunteer in _dbContext.Volunteers.AsNoTracking()
                                         on assignment.VolunteerId equals volunteer.Id
                                     where assignment.Status == AssignmentStatus.Assigned
                                         && slot.IsActive
                                         && shift.IsActive
                                         && shift.PublishedAtUtc.HasValue
                                         && shift.EndsAtUtc > nowUtc
                                     select new HomeExampleRow
                                     {
                                         ShiftId = shift.Id,
                                         SlotId = slot.Id,
                                         ShiftTitle = shift.Title,
                                         SlotLabel = slot.Kind == SlotKind.Primary ? "Primary" : "Backup " + slot.Position,
                                         StartsAtUtc = shift.StartsAtUtc,
                                         EndsAtUtc = shift.EndsAtUtc,
                                         VolunteerName = volunteer.AnonymizedAtUtc.HasValue ? "Removed volunteer" : volunteer.Name,
                                         OccurredAtUtc = assignment.AssignedAtUtc
                                     };

        var unconfirmedAssignmentCount = await unconfirmedAssignments.CountAsync(cancellationToken);
        var unconfirmedAssignmentExamples = (await unconfirmedAssignments
                .OrderBy(x => x.StartsAtUtc)
                .ThenByDescending(x => x.OccurredAtUtc)
                .ThenBy(x => x.ShiftId)
                .ThenBy(x => x.SlotId)
                .Take(3)
                .ToListAsync(cancellationToken))
            .Select(ToHomeExample)
            .ToArray();

        var assignmentFailures = BuildAssignmentFailureQuery(nowUtc);
        var requestFailures = BuildRequestFailureQuery(nowUtc);
        var failedMessageCount =
            await assignmentFailures.CountAsync(cancellationToken) +
            await requestFailures.CountAsync(cancellationToken);
        var failedMessageExamples = (await assignmentFailures
                .Concat(requestFailures)
                .OrderBy(x => x.StartsAtUtc)
                .ThenByDescending(x => x.OccurredAtUtc)
                .ThenBy(x => x.ShiftId)
                .ThenBy(x => x.SlotId)
                .Take(3)
                .ToListAsync(cancellationToken))
            .Select(ToHomeExample)
            .ToArray();

        return new CoordinatorHomeProjection(
            settings,
            hasPublishedShift,
            firstUnpublishedShift,
            pendingRequestCount,
            pendingRequestExamples,
            uncoveredCommitmentCount,
            uncoveredCommitmentExamples,
            unconfirmedAssignmentCount,
            unconfirmedAssignmentExamples,
            failedMessageCount,
            failedMessageExamples,
            firstExpiredUnpublishedShift);
    }

    public async Task<IReadOnlyList<CoordinatorHomeExample>> GetActionableMessageExamplesAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 500);
        var failures = BuildAssignmentFailureQuery(nowUtc)
            .Concat(BuildRequestFailureQuery(nowUtc));
        return (await failures
                .OrderBy(x => x.StartsAtUtc)
                .ThenByDescending(x => x.OccurredAtUtc)
                .ThenBy(x => x.ShiftId)
                .ThenBy(x => x.SlotId)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken))
            .Select(ToHomeExample)
            .ToArray();
    }

    public async Task<CoordinatorMessagePageProjection> GetActionableMessagePageAsync(
        DateTimeOffset nowUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);
        var requestedPage = Math.Max(page, 1);
        var failures = BuildAssignmentFailureQuery(nowUtc)
            .Concat(BuildRequestFailureQuery(nowUtc));
        var totalCount = await failures.CountAsync(cancellationToken);
        var pageCount = totalCount == 0
            ? 1
            : (int)Math.Ceiling(totalCount / (double)boundedPageSize);
        var effectivePage = Math.Min(requestedPage, pageCount);
        var items = (await failures
                .OrderBy(x => x.StartsAtUtc)
                .ThenByDescending(x => x.OccurredAtUtc)
                .ThenBy(x => x.ShiftId)
                .ThenBy(x => x.SlotId)
                .Skip((effectivePage - 1) * boundedPageSize)
                .Take(boundedPageSize)
                .ToListAsync(cancellationToken))
            .Select(ToHomeExample)
            .ToArray();
        return new CoordinatorMessagePageProjection(
            effectivePage,
            boundedPageSize,
            totalCount,
            items);
    }

    private IQueryable<HomeExampleRow> BuildAssignmentFailureQuery(DateTimeOffset nowUtc) =>
        from attempt in _dbContext.NotificationAttempts.AsNoTracking()
        join assignment in _dbContext.Assignments.AsNoTracking()
            on attempt.TransitionId equals assignment.Id
        join slot in _dbContext.ShiftSlots.AsNoTracking()
            on assignment.ShiftSlotId equals slot.Id
        join shift in _dbContext.Shifts.AsNoTracking()
            on assignment.ShiftId equals shift.Id
        join volunteer in _dbContext.Volunteers.AsNoTracking()
            on assignment.VolunteerId equals volunteer.Id
        where attempt.State == NotificationState.Failed
            && slot.IsActive
            && shift.IsActive
            && shift.EndsAtUtc > nowUtc
        select new HomeExampleRow
        {
            ShiftId = shift.Id,
            SlotId = slot.Id,
            ShiftTitle = shift.Title,
            SlotLabel = slot.Kind == SlotKind.Primary ? "Primary" : "Backup " + slot.Position,
            StartsAtUtc = shift.StartsAtUtc,
            EndsAtUtc = shift.EndsAtUtc,
            VolunteerName = volunteer.AnonymizedAtUtc.HasValue ? "Removed volunteer" : volunteer.Name,
            OccurredAtUtc = attempt.CompletedAtUtc ?? attempt.CreatedAtUtc,
            MessageKind = attempt.Kind,
            VolunteerEmail = volunteer.AnonymizedAtUtc.HasValue ? null : volunteer.Email,
            VolunteerPhone = volunteer.AnonymizedAtUtc.HasValue ? null : volunteer.Phone
        };

    private IQueryable<HomeExampleRow> BuildRequestFailureQuery(DateTimeOffset nowUtc) =>
        from attempt in _dbContext.NotificationAttempts.AsNoTracking()
        join request in _dbContext.ShiftRequests.AsNoTracking()
            on attempt.TransitionId equals request.Id
        join slot in _dbContext.ShiftSlots.AsNoTracking()
            on request.ShiftSlotId equals slot.Id
        join shift in _dbContext.Shifts.AsNoTracking()
            on slot.ShiftId equals shift.Id
        join volunteer in _dbContext.Volunteers.AsNoTracking()
            on request.VolunteerId equals volunteer.Id
        where attempt.State == NotificationState.Failed
            && slot.IsActive
            && shift.IsActive
            && shift.EndsAtUtc > nowUtc
        select new HomeExampleRow
        {
            ShiftId = shift.Id,
            SlotId = slot.Id,
            ShiftTitle = shift.Title,
            SlotLabel = slot.Kind == SlotKind.Primary ? "Primary" : "Backup " + slot.Position,
            StartsAtUtc = shift.StartsAtUtc,
            EndsAtUtc = shift.EndsAtUtc,
            VolunteerName = volunteer.AnonymizedAtUtc.HasValue ? "Removed volunteer" : volunteer.Name,
            OccurredAtUtc = attempt.CompletedAtUtc ?? attempt.CreatedAtUtc,
            MessageKind = attempt.Kind,
            VolunteerEmail = volunteer.AnonymizedAtUtc.HasValue ? null : volunteer.Email,
            VolunteerPhone = volunteer.AnonymizedAtUtc.HasValue ? null : volunteer.Phone
        };
    private static CoordinatorHomeExample ToHomeExample(HomeExampleRow row) =>
        new(
            row.ShiftId,
            row.SlotId,
            row.ShiftTitle,
            row.SlotLabel,
            row.StartsAtUtc,
            row.EndsAtUtc,
            row.VolunteerName,
            row.OccurredAtUtc,
            row.MessageKind,
            row.VolunteerEmail,
            row.VolunteerPhone);
    public void AddGroupSettings(GroupSettings settings) => _dbContext.GroupSettings.Add(settings);
    public void AddShift(Shift shift) => _dbContext.Shifts.Add(shift);
    public void AddShiftSlots(IReadOnlyCollection<ShiftSlot> slots) =>
        _dbContext.ShiftSlots.AddRange(slots);



    public void AddVolunteer(Volunteer volunteer) => _dbContext.Volunteers.Add(volunteer);

    public void AddRequest(ShiftRequest request) => _dbContext.ShiftRequests.Add(request);

    public void AddAssignment(Assignment assignment) => _dbContext.Assignments.Add(assignment);

    public void AddActionToken(ActionToken actionToken) => _dbContext.ActionTokens.Add(actionToken);

    public void AddAuditEntry(AuditEntry auditEntry) => _dbContext.AuditEntries.Add(auditEntry);
}
