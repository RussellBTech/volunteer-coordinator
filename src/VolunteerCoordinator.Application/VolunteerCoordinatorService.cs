using System.Text.Json;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Application;

public sealed class VolunteerCoordinatorService
{
    private static readonly TimeSpan StatusTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan ActionTokenLifetime = TimeSpan.FromDays(7);
    private readonly IWorkflowStore _store;
    private readonly IClock _clock;
    private readonly ITokenService _tokens;
    private readonly INotificationService _notifications;

    public VolunteerCoordinatorService(
        IWorkflowStore store,
        IClock clock,
        ITokenService tokens,
        INotificationService notifications)
    {
        _store = store;
        _clock = clock;
        _tokens = tokens;
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken cancellationToken)
    {
        var shifts = await _store.GetAllShiftsAsync(cancellationToken);
        var slotIds = shifts.SelectMany(x => x.Slots).Select(x => x.Id).ToArray();
        var assignments = await _store.GetActiveAssignmentsAsync(slotIds, cancellationToken);
        var assignmentsBySlot = assignments.ToDictionary(x => x.ShiftSlotId);

        return shifts
            .OrderBy(x => x.StartsAtUtc)
            .Select(shift => new ShiftDto(
                shift.Id,
                shift.Title,
                shift.Location,
                shift.Notes,
                shift.StartsAtUtc,
                shift.EndsAtUtc,
                shift.IsActive,
                shift.PublishedAtUtc.HasValue,
                shift.Version,
                shift.Slots.OrderBy(SlotOrder).Select(slot => new SlotDto(
                    slot.Id,
                    slot.Kind.ToString(),
                    slot.Position,
                    !slot.IsActive
                        ? "Inactive"
                        : assignmentsBySlot.TryGetValue(slot.Id, out var assignment)
                            ? assignment.Status.ToString()
                            : "Open")).ToArray()))
            .ToArray();
    }

    public async Task<Guid> CreateShiftAsync(
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        int backupSlotCount,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var shift = Shift.Create(title, location, notes, startsAtUtc, endsAtUtc, backupSlotCount);

        return await _store.ExecuteInTransactionAsync(
            _ =>
            {
                _store.AddShift(shift);
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "ShiftCreated",
                    nameof(Shift),
                    shift.Id,
                    Detail(new { shift.Title, shift.StartsAtUtc, shift.EndsAtUtc, BackupSlots = backupSlotCount })));
                return Task.FromResult(shift.Id);
            },
            cancellationToken);
    }

    public async Task EditShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string title,
        string? location,
        string? notes,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        int backupSlotCount,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException("This shift was changed by another coordinator. Reload it and try again.");
                }

                foreach (var slot in shift.Slots.Where(x =>
                             x.Kind == SlotKind.Backup &&
                             x.IsActive &&
                             x.Position > backupSlotCount))
                {
                    if (await _store.GetActiveAssignmentForSlotAsync(slot.Id, token) is not null ||
                        (await _store.GetPendingRequestsForSlotAsync(slot.Id, token)).Count > 0)
                    {
                        throw new DomainException("A backup slot with an active assignment or pending request cannot be removed.");
                    }
                }

                var existingSlotIds = shift.Slots.Select(slot => slot.Id).ToHashSet();
                shift.Edit(title, location, notes, startsAtUtc, endsAtUtc, now);
                shift.ConfigureBackupSlots(backupSlotCount);
                _store.AddShiftSlots(shift.Slots.Where(slot => !existingSlotIds.Contains(slot.Id)).ToArray());
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "ShiftEdited",
                    nameof(Shift),
                    shift.Id,
                    Detail(new { shift.Title, shift.StartsAtUtc, shift.EndsAtUtc, BackupSlots = backupSlotCount })));
                return true;
            },
            cancellationToken);
    }

    public async Task DeactivateShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException("This shift was changed by another coordinator. Reload it and try again.");
                }

                shift.Deactivate();
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "ShiftDeactivated", nameof(Shift), shift.Id, "{}"));
                return true;
            },
            cancellationToken);
    }

    public async Task PublishShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException("This shift was changed by another coordinator. Reload it and try again.");
                }

                shift.Publish(now);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "ShiftPublished", nameof(Shift), shift.Id, Detail(new { shift.PublishedAtUtc })));
                return true;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<OpeningDto>> ListOpeningsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var shifts = await _store.GetPublishedFutureShiftsAsync(now, cancellationToken);
        var slots = shifts.SelectMany(x => x.Slots).Where(x => x.IsActive).ToArray();
        var assignments = await _store.GetActiveAssignmentsAsync(slots.Select(x => x.Id).ToArray(), cancellationToken);
        var filledSlots = assignments.Select(x => x.ShiftSlotId).ToHashSet();

        return shifts
            .SelectMany(shift => shift.Slots
                .Where(slot => slot.IsActive && !filledSlots.Contains(slot.Id))
                .Select(slot => new OpeningDto(slot.Id, shift.Id, shift.Title, shift.Location, shift.StartsAtUtc, shift.EndsAtUtc, SlotLabel(slot), "Open")))
            .OrderBy(x => x.StartsAtUtc)
            .ThenBy(x => x.SlotLabel)
            .ToArray();
    }

    public async Task<RequestSubmission> SubmitRequestAsync(
        Guid slotId,
        string name,
        string email,
        string? phone,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var generatedToken = _tokens.Generate();
        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var slot = await RequireSlotAsync(slotId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                if (!slot.IsActive || !shift.IsActive || !shift.PublishedAtUtc.HasValue || shift.StartsAtUtc <= now)
                {
                    throw new DomainException("This slot is not open for requests.");
                }

                if (await _store.GetActiveAssignmentForSlotAsync(slot.Id, token) is not null)
                {
                    throw new DomainException("This slot has already been filled.");
                }

                var normalizedEmail = Volunteer.NormalizeEmail(email);
                var volunteer = await _store.GetVolunteerByNormalizedEmailAsync(normalizedEmail, token);
                if (volunteer is null)
                {
                    volunteer = Volunteer.Create(name, email, phone, now);
                    _store.AddVolunteer(volunteer);
                }

                if (await _store.GetPendingRequestAsync(slot.Id, volunteer.Id, token) is not null)
                {
                    throw new DomainException("You already have a pending request for this slot.");
                }

                var request = ShiftRequest.Create(slot.Id, volunteer.Id, generatedToken.Hash, now, now.Add(StatusTokenLifetime));
                _store.AddRequest(request);
                _store.AddAuditEntry(AuditEntry.Create(now, $"volunteer:{volunteer.Id}", "RequestSubmitted", nameof(ShiftRequest), request.Id, Detail(new { request.ShiftSlotId, request.VolunteerId })));
                return (request.Id, volunteer.Email);
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "RequestReceived", result.Email), cancellationToken);
        return new RequestSubmission(result.Id, generatedToken.RawToken, notification.Warning);
    }

    public async Task<RequestStatusDto> GetRequestStatusAsync(string rawStatusToken, CancellationToken cancellationToken)
    {
        var hash = HashRequiredToken(rawStatusToken);
        var request = await _store.GetRequestByStatusHashAsync(hash, cancellationToken);
        if (request is null || !_tokens.FixedTimeEquals(hash, request.StatusTokenHash) || !request.IsStatusTokenUsable(_clock.UtcNow))
        {
            throw new DomainException("This request status link is invalid or has expired.");
        }

        var slot = await RequireSlotAsync(request.ShiftSlotId, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        var volunteer = await RequireVolunteerAsync(request.VolunteerId, cancellationToken);
        var assignment = await _store.GetAssignmentBySourceRequestAsync(request.Id, cancellationToken);

        return new RequestStatusDto(request.Id, volunteer.Name, shift.Title, SlotLabel(slot), shift.StartsAtUtc, request.Status.ToString(), assignment?.Status.ToString());
    }

    public async Task<IReadOnlyList<CoordinatorRequestDto>> ListRequestsAsync(CancellationToken cancellationToken)
    {
        var requests = await _store.GetRequestsAsync(cancellationToken);
        var result = new List<CoordinatorRequestDto>(requests.Count);
        foreach (var request in requests)
        {
            var slot = await RequireSlotAsync(request.ShiftSlotId, cancellationToken);
            var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
            var volunteer = await RequireVolunteerAsync(request.VolunteerId, cancellationToken);
            result.Add(new CoordinatorRequestDto(request.Id, volunteer.Name, volunteer.Email, shift.Title, SlotLabel(slot), shift.StartsAtUtc, request.Status.ToString(), request.RequestedAtUtc));
        }

        return result.OrderByDescending(x => x.RequestedAtUtc).ToArray();
    }

    public async Task<AssignmentResult> ApproveRequestAsync(Guid requestId, string coordinatorEmail, CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var request = await RequireRequestAsync(requestId, token);
                if (request.Status != RequestStatus.Pending)
                {
                    throw new DomainException("Only a pending request can be approved.");
                }

                var slot = await RequireSlotAsync(request.ShiftSlotId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                if (!slot.IsActive || !shift.IsActive || shift.EndsAtUtc <= now)
                {
                    throw new DomainException("A request cannot be approved for an inactive or ended shift.");
                }

                var volunteer = await RequireVolunteerAsync(request.VolunteerId, token);
                await SupersedeConflictingAssignmentsAsync(slot.Id, shift.Id, volunteer.Id, actor, now, token);

                var assignment = Assignment.Create(slot.Id, shift.Id, volunteer.Id, request.Id, actor, now);
                _store.AddAssignment(assignment);
                request.Approve(actor, now);
                await SupersedeOtherRequestsAsync(slot.Id, request.Id, actor, now, token);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "RequestApproved", nameof(ShiftRequest), request.Id, Detail(new { AssignmentId = assignment.Id, assignment.ShiftSlotId, assignment.VolunteerId })));
                return (assignment.Id, volunteer.Email);
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "AssignmentCreated", result.Email), cancellationToken);
        return new AssignmentResult(result.Id, notification.Warning);
    }

    public async Task<CommandResult<bool>> RejectRequestAsync(Guid requestId, string coordinatorEmail, CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var request = await RequireRequestAsync(requestId, token);
                var volunteer = await RequireVolunteerAsync(request.VolunteerId, token);
                request.Reject(actor, now);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "RequestRejected", nameof(ShiftRequest), request.Id, "{}"));
                return (request.Id, volunteer.Email);
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "RequestRejected", result.Email), cancellationToken);
        return new CommandResult<bool>(true, notification.Warning);
    }

    public async Task<AssignmentResult> AssignDirectlyAsync(
        Guid slotId,
        string volunteerName,
        string volunteerEmail,
        string? volunteerPhone,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var slot = await RequireSlotAsync(slotId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                if (!slot.IsActive || !shift.IsActive || shift.EndsAtUtc <= now)
                {
                    throw new DomainException("An inactive or ended slot cannot be assigned.");
                }

                var normalizedEmail = Volunteer.NormalizeEmail(volunteerEmail);
                var volunteer = await _store.GetVolunteerByNormalizedEmailAsync(normalizedEmail, token);
                if (volunteer is null)
                {
                    volunteer = Volunteer.Create(volunteerName, volunteerEmail, volunteerPhone, now);
                    _store.AddVolunteer(volunteer);
                }
                else
                {
                    volunteer.UpdateContact(volunteerName, volunteerEmail, volunteerPhone, now);
                }

                await SupersedeConflictingAssignmentsAsync(slot.Id, shift.Id, volunteer.Id, actor, now, token);
                var assignment = Assignment.Create(slot.Id, shift.Id, volunteer.Id, null, actor, now);
                _store.AddAssignment(assignment);
                await SupersedeOtherRequestsAsync(slot.Id, null, actor, now, token);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "AssignmentCreatedOrReassigned", nameof(Assignment), assignment.Id, Detail(new { assignment.ShiftSlotId, assignment.VolunteerId })));
                return (assignment.Id, volunteer.Email);
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "AssignmentCreated", result.Email), cancellationToken);
        return new AssignmentResult(result.Id, notification.Warning);
    }

    public async Task<IReadOnlyList<VolunteerDto>> ListVolunteersAsync(CancellationToken cancellationToken)
    {
        var volunteers = await _store.GetVolunteersAsync(cancellationToken);
        return volunteers.OrderBy(x => x.Name).Select(x => new VolunteerDto(x.Id, x.Name, x.Email, x.Phone)).ToArray();
    }

    public async Task<ActionLinkBundle> GenerateActionLinksAsync(Guid assignmentId, string coordinatorEmail, CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        return await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var assignment = await RequireAssignmentAsync(assignmentId, token);
                if (!assignment.IsActive)
                {
                    throw new DomainException("Action links can be generated only for an active assignment.");
                }

                var confirm = await RegenerateActionTokenAsync(assignment.Id, VolunteerAction.Confirm, now, token);
                var decline = await RegenerateActionTokenAsync(assignment.Id, VolunteerAction.Decline, now, token);
                var cancel = await RegenerateActionTokenAsync(assignment.Id, VolunteerAction.Cancel, now, token);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "ActionLinksGenerated", nameof(Assignment), assignment.Id, "{}"));
                return new ActionLinkBundle(assignment.Id, confirm, decline, cancel);
            },
            cancellationToken);
    }

    public async Task<ActionInspectionDto> InspectActionAsync(string rawToken, CancellationToken cancellationToken)
    {
        var (token, assignment) = await ResolveActionAsync(rawToken, cancellationToken);
        var slot = await RequireSlotAsync(assignment.ShiftSlotId, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        var volunteer = await RequireVolunteerAsync(assignment.VolunteerId, cancellationToken);
        var canApply = CanApply(token.Action, assignment.Status);
        return new ActionInspectionDto(
            volunteer.Name,
            shift.Title,
            SlotLabel(slot),
            shift.StartsAtUtc,
            token.Action.ToString(),
            assignment.Status.ToString(),
            canApply,
            canApply ? $"This will {token.Action.ToString().ToLowerInvariant()} your assignment." : "This action no longer applies to the current assignment state.");
    }

    public async Task<CommandResult<string>> ApplyActionAsync(string rawToken, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var result = await _store.ExecuteInTransactionAsync(
            async tokenCancellation =>
            {
                var (actionToken, assignment) = await ResolveActionAsync(rawToken, tokenCancellation);
                if (!CanApply(actionToken.Action, assignment.Status))
                {
                    throw new DomainException("This action no longer applies to the current assignment state.");
                }

                actionToken.Consume(now);
                switch (actionToken.Action)
                {
                    case VolunteerAction.Confirm:
                        assignment.Confirm(now);
                        break;
                    case VolunteerAction.Decline:
                        assignment.Decline(now);
                        break;
                    case VolunteerAction.Cancel:
                        assignment.Cancel(now);
                        break;
                    default:
                        throw new DomainException("The volunteer action is not supported.");
                }

                var volunteer = await RequireVolunteerAsync(assignment.VolunteerId, tokenCancellation);
                _store.AddAuditEntry(AuditEntry.Create(now, "volunteer-token", $"Assignment{actionToken.Action}", nameof(Assignment), assignment.Id, Detail(new { assignment.Status })));
                return (assignment.Id, volunteer.Email, Action: actionToken.Action.ToString());
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, $"Assignment{result.Action}", result.Email), cancellationToken);
        return new CommandResult<string>(result.Action, notification.Warning);
    }

    public async Task<IReadOnlyList<CoverageDto>> GetCoverageAsync(CancellationToken cancellationToken)
    {
        var shifts = await _store.GetPublishedFutureShiftsAsync(_clock.UtcNow, cancellationToken);
        var slots = shifts.SelectMany(x => x.Slots).Where(x => x.IsActive).ToArray();
        var assignments = await _store.GetActiveAssignmentsAsync(slots.Select(x => x.Id).ToArray(), cancellationToken);
        var assignmentsBySlot = assignments.ToDictionary(x => x.ShiftSlotId);
        var volunteers = (await _store.GetVolunteersAsync(cancellationToken)).ToDictionary(x => x.Id);
        var result = new List<CoverageDto>(slots.Length);
        foreach (var shift in shifts)
        {
            foreach (var slot in shift.Slots.Where(x => x.IsActive))
            {
                assignmentsBySlot.TryGetValue(slot.Id, out var assignment);
                Volunteer? volunteer = null;
                if (assignment is not null)
                {
                    volunteers.TryGetValue(assignment.VolunteerId, out volunteer);
                }

                var state = assignment?.Status switch
                {
                    AssignmentStatus.Assigned => "Unconfirmed",
                    AssignmentStatus.Confirmed => "Confirmed",
                    _ => "Uncovered"
                };
                result.Add(new CoverageDto(slot.Id, shift.Id, assignment?.Id, shift.Title, SlotLabel(slot), shift.StartsAtUtc, state, volunteer?.Name, volunteer?.Email));
            }
        }

        return result.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.State == "Uncovered" ? 0 : x.State == "Unconfirmed" ? 1 : 2).ThenBy(x => x.SlotLabel).ToArray();
    }

    public async Task<IReadOnlyList<AuditDto>> ListAuditAsync(int limit, CancellationToken cancellationToken)
    {
        var entries = await _store.GetAuditEntriesAsync(Math.Clamp(limit, 1, 500), cancellationToken);
        return entries.Select(x => new AuditDto(x.OccurredAtUtc, x.Actor, x.Action, x.EntityKind, x.EntityId, x.DetailJson)).ToArray();
    }

    private async Task<string> RegenerateActionTokenAsync(Guid assignmentId, VolunteerAction action, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existingTokens = await _store.GetUnusedActionTokensAsync(assignmentId, action, cancellationToken);
        foreach (var existingToken in existingTokens)
        {
            existingToken.Invalidate(now);
        }

        var generated = _tokens.Generate();
        _store.AddActionToken(ActionToken.Create(assignmentId, action, generated.Hash, now, now.Add(ActionTokenLifetime)));
        return generated.RawToken;
    }

    private async Task SupersedeConflictingAssignmentsAsync(
        Guid slotId,
        Guid shiftId,
        Guid volunteerId,
        string coordinatorEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var slotAssignment = await _store.GetActiveAssignmentForSlotAsync(slotId, cancellationToken);
        var volunteerAssignment = await _store.GetActiveAssignmentForVolunteerAndShiftAsync(volunteerId, shiftId, cancellationToken);
        foreach (var assignment in new[] { slotAssignment, volunteerAssignment }.Where(x => x is not null).Cast<Assignment>().DistinctBy(x => x.Id))
        {
            assignment.Reassign(now);
            _store.AddAuditEntry(AuditEntry.Create(
                now,
                coordinatorEmail,
                "AssignmentReassigned",
                nameof(Assignment),
                assignment.Id,
                Detail(new { assignment.ShiftSlotId, assignment.VolunteerId, assignment.Status })));
        }
        await _store.FlushAsync(cancellationToken);
    }

    private async Task SupersedeOtherRequestsAsync(Guid slotId, Guid? approvedRequestId, string coordinatorEmail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var requests = await _store.GetPendingRequestsForSlotAsync(slotId, cancellationToken);
        foreach (var request in requests.Where(x => x.Id != approvedRequestId))
        {
            request.Supersede(coordinatorEmail, now);
        }
    }

    private async Task<(ActionToken Token, Assignment Assignment)> ResolveActionAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = HashRequiredToken(rawToken);
        var actionToken = await _store.GetActionTokenByHashAsync(hash, cancellationToken);
        if (actionToken is null || !_tokens.FixedTimeEquals(hash, actionToken.TokenHash) || !actionToken.IsUsable(_clock.UtcNow))
        {
            throw new DomainException("This action link is invalid, expired, or already used.");
        }

        var assignment = await RequireAssignmentAsync(actionToken.AssignmentId, cancellationToken);
        if (!assignment.IsActive)
        {
            throw new DomainException("This assignment is no longer active.");
        }

        return (actionToken, assignment);
    }

    private byte[] HashRequiredToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new DomainException("A token is required.");
        }

        return _tokens.Hash(rawToken);
    }

    private async Task<NotificationResult> NotifySafelyAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await _notifications.RecordAndSendAsync(message, cancellationToken);
        }
        catch (Exception)
        {
            return new NotificationResult(false, "The workflow succeeded, but notification recording or delivery failed.");
        }
    }

    private async Task<Shift> RequireShiftAsync(Guid id, CancellationToken cancellationToken) =>
        await _store.GetShiftAsync(id, cancellationToken) ?? throw new DomainException("The shift was not found.");

    private async Task<ShiftSlot> RequireSlotAsync(Guid id, CancellationToken cancellationToken) =>
        await _store.GetSlotAsync(id, cancellationToken) ?? throw new DomainException("The shift slot was not found.");

    private async Task<Volunteer> RequireVolunteerAsync(Guid id, CancellationToken cancellationToken) =>
        await _store.GetVolunteerAsync(id, cancellationToken) ?? throw new DomainException("The volunteer was not found.");

    private async Task<ShiftRequest> RequireRequestAsync(Guid id, CancellationToken cancellationToken) =>
        await _store.GetRequestAsync(id, cancellationToken) ?? throw new DomainException("The request was not found.");

    private async Task<Assignment> RequireAssignmentAsync(Guid id, CancellationToken cancellationToken) =>
        await _store.GetAssignmentAsync(id, cancellationToken) ?? throw new DomainException("The assignment was not found.");

    private static bool CanApply(VolunteerAction action, AssignmentStatus status) => action switch
    {
        VolunteerAction.Confirm => status == AssignmentStatus.Assigned,
        VolunteerAction.Decline => status == AssignmentStatus.Assigned,
        VolunteerAction.Cancel => status is AssignmentStatus.Assigned or AssignmentStatus.Confirmed,
        _ => false
    };

    private static string RequireCoordinator(string coordinatorEmail)
    {
        if (string.IsNullOrWhiteSpace(coordinatorEmail))
        {
            throw new DomainException("An authenticated coordinator identity is required.");
        }

        return coordinatorEmail.Trim().ToUpperInvariant();
    }

    private static string SlotLabel(ShiftSlot slot) => slot.Kind == SlotKind.Primary ? "Primary" : $"Backup {slot.Position}";

    private static int SlotOrder(ShiftSlot slot) => slot.Kind == SlotKind.Primary ? 0 : slot.Position;

    private static string Detail<T>(T value) => JsonSerializer.Serialize(value);
}
