using Microsoft.EntityFrameworkCore;
using Npgsql;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Infrastructure.Persistence;

public sealed class EfWorkflowStore : IWorkflowStore
{
    private readonly VolunteerCoordinatorDbContext _dbContext;

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
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new DomainException($"The record changed while it was being saved. Reload and try again. {exception.Message}");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new DomainException("The requested change conflicts with current schedule state. Reload and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<Shift>> GetAllShiftsAsync(CancellationToken cancellationToken) =>
        await _dbContext.Shifts.Include(x => x.Slots).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Shift>> GetPublishedFutureShiftsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        await _dbContext.Shifts
            .Include(x => x.Slots)
            .Where(x => x.IsActive && x.PublishedAtUtc != null && x.StartsAtUtc > nowUtc)
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


    public Task<Volunteer?> GetVolunteerAsync(Guid volunteerId, CancellationToken cancellationToken) =>
        _dbContext.Volunteers.SingleOrDefaultAsync(x => x.Id == volunteerId, cancellationToken);

    public Task<Volunteer?> GetVolunteerByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        _dbContext.Volunteers.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

    public async Task<IReadOnlyList<Volunteer>> GetVolunteersAsync(CancellationToken cancellationToken) =>
        await _dbContext.Volunteers.ToListAsync(cancellationToken);


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

    public void AddShift(Shift shift) => _dbContext.Shifts.Add(shift);
    public void AddShiftSlots(IReadOnlyCollection<ShiftSlot> slots) =>
        _dbContext.ShiftSlots.AddRange(slots);



    public void AddVolunteer(Volunteer volunteer) => _dbContext.Volunteers.Add(volunteer);

    public void AddRequest(ShiftRequest request) => _dbContext.ShiftRequests.Add(request);

    public void AddAssignment(Assignment assignment) => _dbContext.Assignments.Add(assignment);

    public void AddActionToken(ActionToken actionToken) => _dbContext.ActionTokens.Add(actionToken);

    public void AddAuditEntry(AuditEntry auditEntry) => _dbContext.AuditEntries.Add(auditEntry);
}
