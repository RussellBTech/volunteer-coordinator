using System.Text.Json;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;
using VolunteerCoordinator.Application.Time;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Application;

public sealed class VolunteerCoordinatorService
{
    public const string CommitmentUnavailableMessage = "Commitment times are temporarily unavailable. Please contact the coordinator.";
    private const int VolunteerRetentionDays = VolunteerRetentionPolicy.MinimumRetentionDays;
    private const int MaxAssignmentLockAttempts = 3;
    private const string AssignmentLockConflictMessage = "The requested change conflicts with current schedule state. Reload and try again.";
    private static readonly TimeSpan StatusTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan ActionTokenLifetime = TimeSpan.FromDays(7);
    private readonly IWorkflowStore _store;
    private readonly IClock _clock;
    private readonly ITokenService _tokens;
    private readonly INotificationService _notifications;
    private sealed class AssignmentLockRestartException : Exception
    {
    }

    private sealed record VolunteerPrivacyState(
        Volunteer? Volunteer,
        IReadOnlyList<ShiftRequest> Requests,
        IReadOnlyList<Assignment> Assignments,
        IReadOnlyList<Shift> Shifts,
        IReadOnlyList<ActionToken> UnusedActionTokens,
        IReadOnlyList<NotificationAttempt> Notifications);


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

    public async Task<GroupSettingsDto?> GetGroupSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        return settings is null ? null : new GroupSettingsDto(settings.TimeZoneId, settings.Version);
    }

    public async Task<GroupSettingsDto> ConfigureGroupTimeZoneAsync(
        string timeZoneId,
        uint? expectedVersion,
        bool confirmDisplayChange,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        if (!TimeZoneLabels.TryGetIanaZone(timeZoneId, out var normalizedId, out _))
        {
            throw new DomainException("Select a valid IANA time-zone identifier.");
        }

        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.LockGroupSettingsAsync(token);
                var now = _clock.UtcNow;
                var settings = await _store.GetGroupSettingsAsync(token);
                if (settings is null)
                {
                    if (expectedVersion.HasValue)
                    {
                        throw new DomainException("The group time zone was changed while you were configuring it. Reload and try again.");
                    }

                    settings = GroupSettings.Create(normalizedId);
                    _store.AddGroupSettings(settings);
                    _store.AddAuditEntry(AuditEntry.Create(
                        now,
                        actor,
                        "GroupTimeZoneConfigured",
                        nameof(GroupSettings),
                        GroupSettings.SingletonId,
                        Detail(new { NewTimeZoneId = normalizedId })));
                    return true;
                }

                if (!expectedVersion.HasValue || settings.Version != expectedVersion.Value)
                {
                    throw new DomainException("The group time zone was changed by another coordinator. Reload and try again.");
                }

                if (string.Equals(settings.TimeZoneId, normalizedId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!confirmDisplayChange)
                {
                    throw new DomainException(
                        "Changing the group time zone keeps stored UTC instants fixed and changes their displayed local times. Confirm this consequence before saving.");
                }

                var oldId = settings.TimeZoneId;
                settings.Configure(normalizedId);
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "GroupTimeZoneChanged",
                    nameof(GroupSettings),
                    GroupSettings.SingletonId,
                    Detail(new { OldTimeZoneId = oldId, NewTimeZoneId = normalizedId })));
                return true;
            },
            cancellationToken);

        var current = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException("The group time zone could not be loaded after saving.");
        return new GroupSettingsDto(current.TimeZoneId, current.Version);
    }

    public async Task<LocalScheduleResolution> ResolveLocalScheduleAsync(
        LocalScheduleInput input,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return UnconfiguredResolution(input);
        }

        if (settings.Version != input.ExpectedSettingsVersion)
        {
            return ResolutionWithError(
                input,
                "The group time zone changed while this form was open. Reload the form and enter the local times again.");
        }

        return LocalScheduleResolver.Resolve(settings, input);
    }

    public async Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return [];
        }

        var shifts = await _store.GetAllShiftsAsync(cancellationToken);
        var slotIds = shifts.SelectMany(x => x.Slots).Select(x => x.Id).ToArray();
        var assignments = await _store.GetActiveAssignmentsAsync(slotIds, cancellationToken);
        var assignmentsBySlot = assignments.ToDictionary(x => x.ShiftSlotId);

        return shifts
            .OrderBy(x => x.StartsAtUtc)
            .Select(shift => new ShiftDto(
                BuildCommitment(shift, settings, null, "Shift"),
                shift.Notes,
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
        string? volunteerInstructions,
        LocalScheduleInput schedule,
        int backupSlotCount,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        return await _store.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.LockGroupSettingsAsync(token);
                var resolved = await ResolveForMutationAsync(schedule, token);
                var shift = Shift.Create(
                    title,
                    location,
                    notes,
                    volunteerInstructions,
                    resolved.StartsAtUtc,
                    resolved.EndsAtUtc,
                    backupSlotCount);
                _store.AddShift(shift);
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "ShiftCreated",
                    nameof(Shift),
                    shift.Id,
                    Detail(new
                    {
                        shift.Title,
                        shift.StartsAtUtc,
                        shift.EndsAtUtc,
                        BackupSlots = backupSlotCount,
                        HasVolunteerInstructions = shift.VolunteerInstructions is not null
                    })));
                return shift.Id;
            },
            cancellationToken);
    }

    public async Task EditShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string title,
        string? location,
        string? notes,
        string? volunteerInstructions,
        LocalScheduleInput schedule,
        int backupSlotCount,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.LockGroupSettingsAsync(token);
                var resolved = await ResolveForMutationAsync(schedule, token);
                var shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException("This shift was changed by another coordinator. Reload it and try again.");
                }

                var removedBackupSlotIds = shift.Slots
                    .Where(x =>
                        x.Kind == SlotKind.Backup &&
                        x.IsActive &&
                        x.Position > backupSlotCount)
                    .Select(x => x.Id)
                    .ToArray();
                await LockSlotsAsync(removedBackupSlotIds, token);
                await _store.LockShiftAsync(shiftId, token);
                shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException("This shift was changed by another coordinator. Reload it and try again.");
                }
                foreach (var slotId in removedBackupSlotIds)
                {
                    if (await _store.GetActiveAssignmentForSlotAsync(slotId, token) is not null ||
                        (await _store.GetPendingRequestsForSlotAsync(slotId, token)).Count > 0)
                    {
                        throw new DomainException("A backup slot with an active assignment or pending request cannot be removed.");
                    }
                }

                var existingSlotIds = shift.Slots.Select(slot => slot.Id).ToHashSet();
                shift.Edit(
                    title,
                    location,
                    notes,
                    volunteerInstructions,
                    resolved.StartsAtUtc,
                    resolved.EndsAtUtc,
                    now);
                shift.ConfigureBackupSlots(backupSlotCount);
                _store.AddShiftSlots(shift.Slots.Where(slot => !existingSlotIds.Contains(slot.Id)).ToArray());
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "ShiftEdited",
                    nameof(Shift),
                    shift.Id,
                    Detail(new
                    {
                        shift.Title,
                        shift.StartsAtUtc,
                        shift.EndsAtUtc,
                        BackupSlots = backupSlotCount,
                        HasVolunteerInstructions = shift.VolunteerInstructions is not null
                    })));
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

                var slotIds = shift.Slots.Select(x => x.Id).ToArray();
                await LockSlotsAsync(slotIds, token);
                await _store.LockShiftAsync(shiftId, token);
                shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException("This shift was changed by another coordinator. Reload it and try again.");
                }

                var pendingRequests = await _store.GetPendingRequestsAsync(slotIds, token);
                var activeAssignments = await _store.GetActiveAssignmentsAsync(slotIds, token);
                foreach (var request in pendingRequests)
                {
                    request.Supersede(actor, now);
                }

                var invalidatedTokenCount = await CancelAssignmentsAsync(
                    activeAssignments,
                    now,
                    actor,
                    "AssignmentCancelledByShiftDeactivation",
                    token);
                shift.Deactivate();
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "ShiftDeactivated",
                    nameof(Shift),
                    shift.Id,
                    Detail(new
                    {
                        SupersededRequests = pendingRequests.Count,
                        CancelledAssignments = activeAssignments.Count,
                        InvalidatedTokens = invalidatedTokenCount
                    })));
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
                await _store.LockShiftAsync(shiftId, token);
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
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return [];
        }

        var now = _clock.UtcNow;
        var shifts = await _store.GetPublishedFutureShiftsAsync(now, cancellationToken);
        var slots = shifts.SelectMany(x => x.Slots).Where(x => x.IsActive).ToArray();
        var assignments = await _store.GetActiveAssignmentsAsync(slots.Select(x => x.Id).ToArray(), cancellationToken);
        var filledSlots = assignments.Select(x => x.ShiftSlotId).ToHashSet();

        return shifts
            .SelectMany(shift => shift.Slots
                .Where(slot => slot.IsActive && !filledSlots.Contains(slot.Id))
                .Select(slot => new OpeningDto(
                    BuildCommitment(shift, settings, slot, SlotLabel(slot)),
                    "Open")))
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
        if (await _store.GetGroupSettingsAsync(cancellationToken) is null)
        {
            throw new DomainException(CommitmentUnavailableMessage);
        }

        var now = _clock.UtcNow;
        var generatedToken = _tokens.Generate();
        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var settings = await _store.GetGroupSettingsAsync(token)
                    ?? throw new DomainException(CommitmentUnavailableMessage);
                await _store.LockSlotAsync(slotId, token);
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
                var volunteerId = await _store.GetVolunteerIdByNormalizedEmailAsync(normalizedEmail, token);
                Volunteer volunteer;
                if (volunteerId.HasValue)
                {
                    await _store.LockVolunteerAsync(volunteerId.Value, token);
                    volunteer = await RequireVolunteerAsync(volunteerId.Value, token);
                    if (volunteer.AnonymizedAtUtc.HasValue)
                    {
                        throw new DomainException("Removed volunteer contact data cannot be restored.");
                    }
                }
                else
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
                return (request.Id, VolunteerId: volunteer.Id, Commitment: BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "RequestReceived", result.VolunteerId), cancellationToken);
        return new RequestSubmission(result.Id, generatedToken.RawToken, notification.Warning, result.Commitment);
    }

    public async Task<RequestStatusDto> GetRequestStatusAsync(string rawStatusToken, CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
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

        return new RequestStatusDto(
            request.Id,
            volunteer.Name,
            BuildCommitment(shift, settings, slot, SlotLabel(slot)),
            request.Status.ToString(),
            assignment?.Status.ToString());
    }
    public async Task<IReadOnlyList<CoordinatorRequestDto>> ListRequestsAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return [];
        }

        var now = _clock.UtcNow;
        var requests = await _store.GetRequestsAsync(cancellationToken);
        var slotIds = requests.Select(x => x.ShiftSlotId).Distinct().ToArray();
        var assignmentsBySlot = (await _store.GetActiveAssignmentsAsync(slotIds, cancellationToken))
            .ToDictionary(x => x.ShiftSlotId);
        var result = new List<CoordinatorRequestDto>(requests.Count);
        foreach (var request in requests)
        {
            var slot = await RequireSlotAsync(request.ShiftSlotId, cancellationToken);
            var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
            var volunteer = await RequireVolunteerAsync(request.VolunteerId, cancellationToken);
            assignmentsBySlot.TryGetValue(slot.Id, out var assignment);
            var slotState = !slot.IsActive || !shift.IsActive
                ? "Inactive"
                : shift.EndsAtUtc <= now
                    ? "Ended"
                    : assignment?.Status switch
                    {
                        AssignmentStatus.Assigned => "Unconfirmed",
                        AssignmentStatus.Confirmed => "Confirmed",
                        _ => "Available"
                    };
            var canApprove = request.Status == RequestStatus.Pending && slotState == "Available";
            result.Add(new CoordinatorRequestDto(
                request.Id,
                volunteer.AnonymizedAtUtc.HasValue ? "Removed volunteer" : volunteer.Name,
                volunteer.AnonymizedAtUtc.HasValue ? string.Empty : volunteer.Email,
                BuildCommitment(shift, settings, slot, SlotLabel(slot)),
                request.Status.ToString(),
                request.RequestedAtUtc,
                canApprove,
                slotState));
        }

        return result.OrderByDescending(x => x.RequestedAtUtc).ToArray();
    }


    public async Task<AssignmentResult> ApproveRequestAsync(Guid requestId, string coordinatorEmail, CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var result = await ExecuteWithAssignmentLockRetryAsync(
            async token =>
            {
                var request = await RequireRequestAsync(requestId, token);
                if (request.Status != RequestStatus.Pending)
                {
                    throw new DomainException("Only a pending request can be approved.");
                }

                var preflightSlot = await RequireSlotAsync(request.ShiftSlotId, token);
                var preflightShift = await RequireShiftAsync(preflightSlot.ShiftId, token);
                var preflightVolunteer = await RequireVolunteerAsync(request.VolunteerId, token);
                var lockedAssignmentSlots = await LockAssignmentSlotsAsync(
                    preflightSlot.Id,
                    preflightShift.Id,
                    preflightVolunteer.Id,
                    token);
                await _store.LockVolunteerAsync(preflightVolunteer.Id, token);

                request = await RequireRequestAsync(requestId, token);
                if (request.Status != RequestStatus.Pending)
                {
                    throw new DomainException("Only a pending request can be approved.");
                }

                var slot = await RequireSlotAsync(request.ShiftSlotId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                var settings = await _store.GetGroupSettingsAsync(token)
                    ?? throw new DomainException(CommitmentUnavailableMessage);
                if (!slot.IsActive || !shift.IsActive || shift.EndsAtUtc <= now)
                {
                    throw new DomainException("A request cannot be approved for an inactive or ended shift.");
                }

                var volunteer = await RequireVolunteerAsync(request.VolunteerId, token);
                if (volunteer.AnonymizedAtUtc.HasValue)
                {
                    throw new DomainException("Removed volunteer contact data cannot be restored.");
                }
                await SupersedeConflictingAssignmentsAsync(
                    slot.Id,
                    shift.Id,
                    volunteer.Id,
                    lockedAssignmentSlots,
                    actor,
                    now,
                    token);

                var assignment = Assignment.Create(slot.Id, shift.Id, volunteer.Id, request.Id, actor, now);
                _store.AddAssignment(assignment);
                request.Approve(actor, now);
                await SupersedeOtherRequestsAsync(slot.Id, request.Id, actor, now, token);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "RequestApproved", nameof(ShiftRequest), request.Id, Detail(new { AssignmentId = assignment.Id, assignment.ShiftSlotId, assignment.VolunteerId })));
                return (assignment.Id, VolunteerId: volunteer.Id, Commitment: BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            },
            cancellationToken);




        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "AssignmentCreated", result.VolunteerId), cancellationToken);
        return new AssignmentResult(result.Id, notification.Warning, result.Commitment);
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
                return (request.Id, VolunteerId: volunteer.Id);
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "RequestRejected", result.VolunteerId), cancellationToken);
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
        var result = await ExecuteWithAssignmentLockRetryAsync(
            async token =>
            {
                var preflightSlot = await RequireSlotAsync(slotId, token);
                var preflightShift = await RequireShiftAsync(preflightSlot.ShiftId, token);
                var normalizedEmail = Volunteer.NormalizeEmail(volunteerEmail);
                var preflightVolunteerId = await _store.GetVolunteerIdByNormalizedEmailAsync(normalizedEmail, token);
                var lockedAssignmentSlots = await LockAssignmentSlotsAsync(
                    preflightSlot.Id,
                    preflightShift.Id,
                    preflightVolunteerId,
                    token);
                if (preflightVolunteerId.HasValue)
                {
                    await _store.LockVolunteerAsync(preflightVolunteerId.Value, token);
                }

                var slot = await RequireSlotAsync(slotId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                var settings = await _store.GetGroupSettingsAsync(token)
                    ?? throw new DomainException(CommitmentUnavailableMessage);
                if (!slot.IsActive || !shift.IsActive || shift.EndsAtUtc <= now)
                {
                    throw new DomainException("An inactive or ended slot cannot be assigned.");
                }

                var volunteerId = await _store.GetVolunteerIdByNormalizedEmailAsync(normalizedEmail, token);
                Volunteer volunteer;
                if (volunteerId.HasValue)
                {
                    if (!preflightVolunteerId.HasValue || volunteerId.Value != preflightVolunteerId.Value)
                    {
                        await _store.LockVolunteerAsync(volunteerId.Value, token);
                    }

                    volunteer = await RequireVolunteerAsync(volunteerId.Value, token);
                    volunteer.UpdateContact(volunteerName, volunteerEmail, volunteerPhone, now);
                }
                else
                {
                    volunteer = Volunteer.Create(volunteerName, volunteerEmail, volunteerPhone, now);
                    _store.AddVolunteer(volunteer);
                }

                await SupersedeConflictingAssignmentsAsync(
                    slot.Id,
                    shift.Id,
                    volunteer.Id,
                    lockedAssignmentSlots,
                    actor,
                    now,
                    token);
                var assignment = Assignment.Create(slot.Id, shift.Id, volunteer.Id, null, actor, now);
                _store.AddAssignment(assignment);
                await SupersedeOtherRequestsAsync(slot.Id, null, actor, now, token);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "AssignmentCreatedOrReassigned", nameof(Assignment), assignment.Id, Detail(new { assignment.ShiftSlotId, assignment.VolunteerId })));
                return (assignment.Id, VolunteerId: volunteer.Id, Commitment: BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            },
            cancellationToken);
        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "AssignmentCreated", result.VolunteerId), cancellationToken);
        return new AssignmentResult(result.Id, notification.Warning, result.Commitment);
    }


    public async Task CancelAssignmentAsync(
        Guid assignmentId,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var slotId = await _store.GetAssignmentSlotIdAsync(assignmentId, token)
                    ?? throw new DomainException("The assignment was not found.");
                await _store.LockSlotAsync(slotId, token);
                var assignment = await RequireAssignmentAsync(assignmentId, token);
                if (!assignment.IsActive)
                {
                    throw new DomainException("Only an active assignment can be cancelled.");
                }

                await CancelAssignmentsAsync(
                    [assignment],
                    now,
                    actor,
                    "AssignmentCancelledByCoordinator",
                    token);
                return true;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<VolunteerDto>> ListVolunteersAsync(CancellationToken cancellationToken)
    {
        var volunteers = await _store.GetVolunteersAsync(cancellationToken);
        return volunteers
            .OrderBy(x => x.Name)
            .Select(x => new VolunteerDto(
                x.Id,
                x.AnonymizedAtUtc.HasValue ? "Removed volunteer" : x.Name,
                x.AnonymizedAtUtc.HasValue ? string.Empty : x.Email,
                x.AnonymizedAtUtc.HasValue ? null : x.Phone)
            {
                IsAnonymized = x.AnonymizedAtUtc.HasValue
            })
            .ToArray();
    }

    public async Task<VolunteerAnonymizationResult> AnonymizeVolunteerAsync(
        Guid volunteerId,
        VolunteerAnonymizationReason reason,
        string actor,
        CancellationToken cancellationToken)
    {
        var auditActor = reason == VolunteerAnonymizationReason.RetentionExpired
            ? "retention-worker"
            : RequireCoordinator(actor);
        return await _store.ExecuteInTransactionAsync(
            token => AnonymizeVolunteerInTransactionAsync(
                volunteerId,
                reason,
                auditActor,
                VolunteerRetentionDays,
                token),
            cancellationToken);
    }

    public async Task<IReadOnlyList<RetentionSweepResult>> RunRetentionSweepAsync(
        int retentionDays,
        int batchSize,
        CancellationToken cancellationToken,
        Guid? afterVolunteerId = null) =>
        await RunRetentionSweepCoreAsync(
            retentionDays,
            batchSize,
            afterVolunteerId,
            cancellationToken);

    public async Task RunInitialRetentionSweepAsync(
        int retentionDays,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ValidateRetentionSettings(retentionDays, batchSize);
        Guid? afterVolunteerId = null;
        while (true)
        {
            var results = await RunRetentionSweepCoreAsync(
                retentionDays,
                batchSize,
                afterVolunteerId,
                cancellationToken);
            if (results.Count == 0)
            {
                return;
            }

            afterVolunteerId = results[^1].VolunteerId;
        }
    }

    private async Task<IReadOnlyList<RetentionSweepResult>> RunRetentionSweepCoreAsync(
        int retentionDays,
        int batchSize,
        Guid? afterVolunteerId,
        CancellationToken cancellationToken)
    {
        ValidateRetentionSettings(retentionDays, batchSize);

        var now = _clock.UtcNow;
        var candidateIds = await _store.GetRetentionCandidateIdsAsync(
            now.Subtract(TimeSpan.FromDays(retentionDays)),
            afterVolunteerId,
            batchSize,
            cancellationToken);
        var results = new List<RetentionSweepResult>(candidateIds.Count);
        foreach (var candidateId in candidateIds)
        {
            try
            {
                var result = await _store.ExecuteInTransactionAsync(
                    token => AnonymizeVolunteerInTransactionAsync(
                        candidateId,
                        VolunteerAnonymizationReason.RetentionExpired,
                        "retention-worker",
                        retentionDays,
                        token),
                    cancellationToken);
                results.Add(new RetentionSweepResult(candidateId, result, false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DomainException exception)
            {
                results.Add(new RetentionSweepResult(
                    candidateId,
                    null,
                    true,
                    RetentionFailureReason(exception)));
            }
        }

        return results;
    }

    private static string RetentionFailureReason(DomainException exception)
    {
        var reason = exception.Message.Trim();
        return string.IsNullOrEmpty(reason)
            ? "Domain validation failed."
            : reason.Length <= 500
                ? reason
                : reason[..500];
    }

    public async Task<VolunteerRemovalLookup?> LookupVolunteerRemovalAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = Volunteer.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        var projection = await _store.GetVolunteerRemovalProjectionByNormalizedEmailAsync(
            normalizedEmail,
            cancellationToken);
        var settings = projection is null
            ? null
            : await _store.GetGroupSettingsAsync(cancellationToken);
        if (projection is null || settings is null)
        {
            return null;
        }

        return new VolunteerRemovalLookup(
            projection.VolunteerId,
            projection.Commitments
                .Select(commitment => new VolunteerRemovalCommitment(
                    commitment.ShiftId,
                    new CommitmentDto(
                        commitment.ShiftId,
                        commitment.SlotId,
                        commitment.ShiftTitle,
                        commitment.StartsAtUtc,
                        commitment.EndsAtUtc,
                        settings.TimeZoneId,
                        commitment.Location,
                        SlotLabel(commitment.SlotKind, commitment.SlotPosition),
                        commitment.VolunteerInstructions),
                    commitment.AssignmentStatus.HasValue
                        ? $"Assignment {commitment.AssignmentStatus.Value}"
                        : $"Request {commitment.RequestStatus!.Value}"))
                .ToArray());
    }

    public async Task<VolunteerAnonymizationResult> ConfirmVolunteerRemovalAsync(
        Guid volunteerId,
        Guid selectedShiftId,
        string normalizedEmail,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var expectedNormalizedEmail = Volunteer.NormalizeEmail(normalizedEmail);
        if (string.IsNullOrWhiteSpace(expectedNormalizedEmail))
        {
            return new VolunteerAnonymizationResult(
                VolunteerAnonymizationOutcome.NotFound,
                VolunteerAnonymizationBlocker.None,
                0,
                null,
                0,
                0,
                0);
        }

        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.LockVolunteerAsync(volunteerId, token);
                var state = await LoadVolunteerPrivacyStateAsync(volunteerId, token);
                if (state.Volunteer is null ||
                    state.Volunteer.AnonymizedAtUtc.HasValue ||
                    !string.Equals(
                        state.Volunteer.NormalizedEmail,
                        expectedNormalizedEmail,
                        StringComparison.Ordinal))
                {
                    return new VolunteerAnonymizationResult(
                        VolunteerAnonymizationOutcome.NotFound,
                        VolunteerAnonymizationBlocker.None,
                        0,
                        null,
                        0,
                        0,
                        0);
                }

                var boundedLookup = await _store
                    .GetVolunteerRemovalProjectionByNormalizedEmailAsync(
                        expectedNormalizedEmail,
                        token);
                if (boundedLookup is null ||
                    boundedLookup.VolunteerId != volunteerId ||
                    !boundedLookup.Commitments.Any(commitment => commitment.ShiftId == selectedShiftId))
                {
                    return new VolunteerAnonymizationResult(
                        VolunteerAnonymizationOutcome.NotFound,
                        VolunteerAnonymizationBlocker.None,
                        0,
                        null,
                        0,
                        0,
                        0);
                }

                return await AnonymizeVolunteerInTransactionAsync(
                    volunteerId,
                    VolunteerAnonymizationReason.CoordinatorRequest,
                    actor,
                    VolunteerRetentionDays,
                    token,
                    state);
            },
            cancellationToken);
        return result;
    }
    public async Task<ActionLinkBundle> GenerateActionLinksAsync(Guid assignmentId, string coordinatorEmail, CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        return await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var slotId = await _store.GetAssignmentSlotIdAsync(assignmentId, token)
                    ?? throw new DomainException("The assignment was not found.");
                await _store.LockSlotAsync(slotId, token);
                var assignment = await RequireAssignmentAsync(assignmentId, token);
                if (!assignment.IsActive)
                {
                    throw new DomainException("Action links can be generated only for an active assignment.");
                }

                var settings = await _store.GetGroupSettingsAsync(token)
                    ?? throw new DomainException(CommitmentUnavailableMessage);
                var slot = await RequireSlotAsync(assignment.ShiftSlotId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                string? confirm = null;
                string? decline = null;
                if (assignment.Status == AssignmentStatus.Assigned)
                {
                    confirm = await RegenerateActionTokenAsync(assignment.Id, VolunteerAction.Confirm, now, token);
                    decline = await RegenerateActionTokenAsync(assignment.Id, VolunteerAction.Decline, now, token);
                }

                var cancel = await RegenerateActionTokenAsync(assignment.Id, VolunteerAction.Cancel, now, token);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "ActionLinksGenerated", nameof(Assignment), assignment.Id, "{}"));
                return new ActionLinkBundle(
                    assignment.Id,
                    confirm,
                    decline,
                    cancel,
                    BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            },
            cancellationToken);
    }

    public async Task<ActionInspectionDto> InspectActionAsync(string rawToken, CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var (token, assignment) = await ResolveActionAsync(rawToken, cancellationToken);
        var slot = await RequireSlotAsync(assignment.ShiftSlotId, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        var volunteer = await RequireVolunteerAsync(assignment.VolunteerId, cancellationToken);
        var canApply = CanApply(token.Action, assignment.Status);
        return new ActionInspectionDto(
            volunteer.Name,
            BuildCommitment(shift, settings, slot, SlotLabel(slot)),
            token.Action.ToString(),
            assignment.Status.ToString(),
            canApply,
            canApply ? $"This will {token.Action.ToString().ToLowerInvariant()} your assignment." : "This action no longer applies to the current assignment state.");
    }

    public async Task<CommandResult<string>> ApplyActionAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (await _store.GetGroupSettingsAsync(cancellationToken) is null)
        {
            throw new DomainException(CommitmentUnavailableMessage);
        }

        var now = _clock.UtcNow;
        var hash = HashRequiredToken(rawToken);
        var result = await _store.ExecuteInTransactionAsync(
            async tokenCancellation =>
            {
                _ = await _store.GetGroupSettingsAsync(tokenCancellation)
                    ?? throw new DomainException(CommitmentUnavailableMessage);
                var slotId = await _store.GetActionTokenSlotIdAsync(hash, tokenCancellation);
                if (!slotId.HasValue)
                {
                    throw new DomainException("This action link is invalid, expired, or already used.");
                }

                await _store.LockSlotAsync(slotId.Value, tokenCancellation);
                var (actionToken, assignment) = await ResolveActionAsync(hash, tokenCancellation);
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
                return (assignment.Id, VolunteerId: volunteer.Id, Action: actionToken.Action.ToString());
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, $"Assignment{result.Action}", result.VolunteerId), cancellationToken);
        return new CommandResult<string>(result.Action, notification.Warning);
    }


    public async Task<IReadOnlyList<CoverageDto>> GetCoverageAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return [];
        }

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
                result.Add(new CoverageDto(
                    slot.Id,
                    shift.Id,
                    assignment?.Id,
                    BuildCommitment(shift, settings, slot, SlotLabel(slot)),
                    state,
                    volunteer?.AnonymizedAtUtc.HasValue == true ? "Removed volunteer" : volunteer?.Name,
                    volunteer?.AnonymizedAtUtc.HasValue == true ? null : volunteer?.Email));
            }
        }

        return result.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.State == "Uncovered" ? 0 : x.State == "Unconfirmed" ? 1 : 2).ThenBy(x => x.SlotLabel).ToArray();
    }

    public async Task<IReadOnlyList<AuditDto>> ListAuditAsync(int limit, CancellationToken cancellationToken)
    {
        var entries = await _store.GetAuditEntriesAsync(Math.Clamp(limit, 1, 500), cancellationToken);
        return entries.Select(x => new AuditDto(x.OccurredAtUtc, x.Actor, x.Action, x.EntityKind, x.EntityId, x.DetailJson)).ToArray();
    }

    private async Task<VolunteerAnonymizationResult> AnonymizeVolunteerInTransactionAsync(
        Guid volunteerId,
        VolunteerAnonymizationReason reason,
        string actor,
        int retentionDays,
        CancellationToken cancellationToken,
        VolunteerPrivacyState? knownState = null)
    {
        var state = knownState;
        if (state is null)
        {
            await _store.LockVolunteerAsync(volunteerId, cancellationToken);
            state = await LoadVolunteerPrivacyStateAsync(volunteerId, cancellationToken);
        }

        if (state.Volunteer is null)
        {
            return new VolunteerAnonymizationResult(
                VolunteerAnonymizationOutcome.NotFound,
                VolunteerAnonymizationBlocker.None,
                0,
                null,
                0,
                0,
                0);
        }

        if (state.Volunteer.AnonymizedAtUtc.HasValue)
        {
            return new VolunteerAnonymizationResult(
                VolunteerAnonymizationOutcome.AlreadyAnonymized,
                VolunteerAnonymizationBlocker.None,
                0,
                state.Volunteer.AnonymizedAtUtc,
                0,
                0,
                0);
        }

        var now = _clock.UtcNow;
        var pendingCount = state.Requests.Count(x => x.Status == RequestStatus.Pending);
        if (pendingCount > 0)
        {
            return BlockedResult(
                VolunteerAnonymizationBlocker.PendingRequests,
                pendingCount,
                CalculatePrivacyAnchor(state));
        }

        var activeAssignmentCount = state.Assignments.Count(x => x.IsActive);
        if (activeAssignmentCount > 0)
        {
            return BlockedResult(
                VolunteerAnonymizationBlocker.ActiveAssignments,
                activeAssignmentCount,
                CalculatePrivacyAnchor(state));
        }

        var futureCommitmentCount = state.Shifts
            .Where(x => x.EndsAtUtc > now)
            .Select(x => x.Id)
            .Distinct()
            .Count();
        if (futureCommitmentCount > 0)
        {
            return BlockedResult(
                VolunteerAnonymizationBlocker.FutureCommitments,
                futureCommitmentCount,
                CalculatePrivacyAnchor(state));
        }

        var anchor = CalculatePrivacyAnchor(state);
        if (reason == VolunteerAnonymizationReason.RetentionExpired &&
            anchor > now.Subtract(TimeSpan.FromDays(retentionDays)))
        {
            return BlockedResult(
                VolunteerAnonymizationBlocker.TooRecent,
                1,
                anchor);
        }

        var invalidatedStatusTokens = 0;
        foreach (var request in state.Requests)
        {
            if (request.InvalidateStatusToken(now))
            {
                invalidatedStatusTokens++;
            }
        }

        var invalidatedActionTokens = 0;
        foreach (var actionToken in state.UnusedActionTokens)
        {
            actionToken.Invalidate(now);
            invalidatedActionTokens++;
        }

        var redactedNotifications = 0;
        foreach (var notification in state.Notifications)
        {
            if (notification.RedactDestination())
            {
                redactedNotifications++;
            }
        }

        if (!state.Volunteer.Anonymize(now))
        {
            return new VolunteerAnonymizationResult(
                VolunteerAnonymizationOutcome.AlreadyAnonymized,
                VolunteerAnonymizationBlocker.None,
                0,
                anchor,
                0,
                0,
                0);
        }

        _store.AddAuditEntry(AuditEntry.Create(
            now,
            actor,
            "VolunteerAnonymized",
            nameof(Volunteer),
            state.Volunteer.Id,
            Detail(new
            {
                VolunteerId = state.Volunteer.Id,
                Reason = reason.ToString(),
                AnchorUtc = anchor,
                RequestCount = state.Requests.Count,
                AssignmentCount = state.Assignments.Count,
                StatusTokensInvalidated = invalidatedStatusTokens,
                ActionTokensInvalidated = invalidatedActionTokens,
                NotificationDestinationsRedacted = redactedNotifications
            })));

        return new VolunteerAnonymizationResult(
            VolunteerAnonymizationOutcome.Anonymized,
            VolunteerAnonymizationBlocker.None,
            0,
            anchor,
            invalidatedStatusTokens,
            invalidatedActionTokens,
            redactedNotifications)
        {
            RequestCount = state.Requests.Count,
            AssignmentCount = state.Assignments.Count
        };
    }

    private async Task<VolunteerPrivacyState> LoadVolunteerPrivacyStateAsync(
        Guid volunteerId,
        CancellationToken cancellationToken)
    {
        var volunteer = await _store.GetVolunteerAsync(volunteerId, cancellationToken);
        if (volunteer is null)
        {
            return new VolunteerPrivacyState(null, [], [], [], [], []);
        }

        var requests = await _store.GetRequestsForVolunteerAsync(volunteerId, cancellationToken);
        var assignments = await _store.GetAssignmentsForVolunteerAsync(volunteerId, cancellationToken);
        var slotIds = requests
            .Select(x => x.ShiftSlotId)
            .Concat(assignments.Select(x => x.ShiftSlotId))
            .Distinct()
            .ToArray();
        var shifts = await _store.GetShiftsForSlotIdsAsync(slotIds, cancellationToken);
        var orderedShiftIds = shifts
            .Select(x => x.Id)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        foreach (var shiftId in orderedShiftIds)
        {
            await _store.LockShiftAsync(shiftId, cancellationToken);
        }

        // Schedule mutations use the same shift-row lock. Re-read after every related
        // shift is locked so timing and anchor checks cannot use a pre-lock snapshot.
        shifts = await _store.GetShiftsForSlotIdsAsync(slotIds, cancellationToken);
        var transitionIds = requests
            .Select(x => x.Id)
            .Concat(assignments.Select(x => x.Id))
            .ToArray();
        var notifications = await _store.GetNotificationAttemptsAsync(transitionIds, cancellationToken);
        var actionTokens = await _store.GetUnusedActionTokensAsync(
            assignments.Select(x => x.Id).ToArray(),
            cancellationToken);
        return new VolunteerPrivacyState(
            volunteer,
            requests,
            assignments,
            shifts,
            actionTokens,
            notifications);
    }




    private static DateTimeOffset CalculatePrivacyAnchor(VolunteerPrivacyState state)
    {
        var anchor = state.Volunteer?.UpdatedAtUtc ?? DateTimeOffset.MinValue;
        foreach (var request in state.Requests)
        {
            anchor = Max(anchor, request.RequestedAtUtc);
            anchor = Max(anchor, request.ResolvedAtUtc);
        }

        foreach (var assignment in state.Assignments)
        {
            anchor = Max(anchor, assignment.AssignedAtUtc);
            anchor = Max(anchor, assignment.ConfirmedAtUtc);
            anchor = Max(anchor, assignment.EndedAtUtc);
        }

        foreach (var notification in state.Notifications)
        {
            anchor = Max(anchor, notification.CreatedAtUtc);
            anchor = Max(anchor, notification.CompletedAtUtc);
        }

        foreach (var shift in state.Shifts)
        {
            anchor = Max(anchor, shift.EndsAtUtc);
        }

        return anchor;
    }

    private static DateTimeOffset Max(DateTimeOffset current, DateTimeOffset? candidate) =>
        candidate.HasValue && candidate.Value > current ? candidate.Value : current;

    private static VolunteerAnonymizationResult BlockedResult(
        VolunteerAnonymizationBlocker blocker,
        int count,
        DateTimeOffset anchor) =>
        new(
            VolunteerAnonymizationOutcome.Blocked,
            blocker,
            count,
            anchor,
            0,
            0,
            0);



    private async Task<int> CancelAssignmentsAsync(
        IReadOnlyCollection<Assignment> assignments,
        DateTimeOffset now,
        string coordinatorEmail,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var tokens = await _store.GetUnusedActionTokensAsync(
            assignments.Select(x => x.Id).ToArray(),
            cancellationToken);
        var tokensByAssignment = tokens.ToLookup(x => x.AssignmentId);
        foreach (var assignment in assignments)
        {
            assignment.Cancel(now);
            foreach (var actionToken in tokensByAssignment[assignment.Id])
            {
                actionToken.Invalidate(now);
            }

            _store.AddAuditEntry(AuditEntry.Create(
                now,
                coordinatorEmail,
                auditAction,
                nameof(Assignment),
                assignment.Id,
                Detail(new { assignment.ShiftSlotId, assignment.VolunteerId, assignment.Status })));
        }

        return tokens.Count;
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
    private async Task<T> ExecuteWithAssignmentLockRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (attempts < MaxAssignmentLockAttempts)
        {
            attempts++;
            try
            {
                return await _store.ExecuteInTransactionAsync(operation, cancellationToken);
            }
            catch (AssignmentLockRestartException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        throw new DomainException(AssignmentLockConflictMessage);
    }

    private async Task<IReadOnlySet<Guid>> LockSlotsAsync(
        IEnumerable<Guid> slotIds,
        CancellationToken cancellationToken)
    {
        var orderedSlotIds = slotIds
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        foreach (var slotId in orderedSlotIds)
        {
            await _store.LockSlotAsync(slotId, cancellationToken);
        }

        return orderedSlotIds.ToHashSet();
    }


    private async Task<IReadOnlySet<Guid>> LockAssignmentSlotsAsync(
        Guid destinationSlotId,
        Guid shiftId,
        Guid? volunteerId,
        CancellationToken cancellationToken)
    {
        var slotAssignment = await _store.GetActiveAssignmentForSlotAsync(destinationSlotId, cancellationToken);
        var volunteerAssignment = volunteerId.HasValue
            ? await _store.GetActiveAssignmentForVolunteerAndShiftAsync(volunteerId.Value, shiftId, cancellationToken)
            : null;
        var slotIds = new Guid?[] { destinationSlotId, slotAssignment?.ShiftSlotId, volunteerAssignment?.ShiftSlotId }
            .Where(x => x.HasValue)
            .Select(x => x.GetValueOrDefault())
            .Distinct()
            .ToArray();
        var lockedSlotIds = await LockSlotsAsync(slotIds, cancellationToken);

        var currentSlotAssignment = await _store.GetActiveAssignmentForSlotAsync(destinationSlotId, cancellationToken);
        var currentVolunteerAssignment = volunteerId.HasValue
            ? await _store.GetActiveAssignmentForVolunteerAndShiftAsync(volunteerId.Value, shiftId, cancellationToken)
            : null;
        var currentSlotIds = new Guid?[]
        {
            currentSlotAssignment?.ShiftSlotId,
            currentVolunteerAssignment?.ShiftSlotId
        };
        if (currentSlotIds.Any(x => x.HasValue && !lockedSlotIds.Contains(x.Value)))
        {
            throw new AssignmentLockRestartException();
        }

        return lockedSlotIds;
    }

    private async Task SupersedeConflictingAssignmentsAsync(
        Guid slotId,
        Guid shiftId,
        Guid volunteerId,
        IReadOnlySet<Guid> lockedSlotIds,
        string coordinatorEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var slotAssignment = await _store.GetActiveAssignmentForSlotAsync(slotId, cancellationToken);
        var volunteerAssignment = await _store.GetActiveAssignmentForVolunteerAndShiftAsync(volunteerId, shiftId, cancellationToken);
        var conflictingAssignments = new[] { slotAssignment, volunteerAssignment }
            .Where(x => x is not null)
            .Cast<Assignment>()
            .DistinctBy(x => x.Id)
            .ToArray();
        if (conflictingAssignments.Any(assignment => !lockedSlotIds.Contains(assignment.ShiftSlotId)))
        {
            throw new AssignmentLockRestartException();
        }

        foreach (var assignment in conflictingAssignments)
        {
            assignment.Reassign(now);
            var actionTokens = await _store.GetUnusedActionTokensAsync([assignment.Id], cancellationToken);
            foreach (var actionToken in actionTokens)
            {
                actionToken.Invalidate(now);
            }

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

    private async Task<(ActionToken Token, Assignment Assignment)> ResolveActionAsync(string rawToken, CancellationToken cancellationToken) =>
        await ResolveActionAsync(HashRequiredToken(rawToken), cancellationToken);

    private async Task<(ActionToken Token, Assignment Assignment)> ResolveActionAsync(byte[] hash, CancellationToken cancellationToken)
    {
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

    public async Task<CommitmentDto> GetAssignmentCommitmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var slotId = await _store.GetAssignmentSlotIdAsync(assignmentId, cancellationToken)
            ?? throw new DomainException("The assignment was not found.");
        var slot = await RequireSlotAsync(slotId, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        return BuildCommitment(shift, settings, slot, SlotLabel(slot));
    }

    private async Task<(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc)> ResolveForMutationAsync(
        LocalScheduleInput input,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        if (settings.Version != input.ExpectedSettingsVersion)
        {
            throw new DomainException(
                "The group time zone changed while this form was open. Reload the form and enter the local times again.");
        }

        var resolution = LocalScheduleResolver.Resolve(settings, input);
        if (!resolution.IsComplete)
        {
            var messages = resolution.Errors.ToList();
            if (resolution.StartCandidates.Count > 0 || resolution.EndCandidates.Count > 0)
            {
                messages.Add("Choose one labelled UTC-offset interpretation for each repeated local time.");
            }

            throw new DomainException(
                messages.Count == 0
                    ? "Choose valid local start and end times."
                    : string.Join(" ", messages.Distinct(StringComparer.Ordinal)));
        }

        return (resolution.StartsAtUtc.GetValueOrDefault(), resolution.EndsAtUtc.GetValueOrDefault());
    }


    private static CommitmentDto BuildCommitment(
        Shift shift,
        GroupSettings settings,
        ShiftSlot? slot,
        string slotLabel) =>
        new(
            shift.Id,
            slot?.Id,
            shift.Title,
            shift.StartsAtUtc,
            shift.EndsAtUtc,
            settings.TimeZoneId,
            shift.Location,
            slotLabel,
            shift.VolunteerInstructions);

    private static LocalScheduleResolution UnconfiguredResolution(LocalScheduleInput input) =>
        ResolutionWithError(input, CommitmentUnavailableMessage);

    private static LocalScheduleResolution ResolutionWithError(
        LocalScheduleInput input,
        string error) =>
        new(
            input.StartsAtUnspecified,
            input.EndsAtUnspecified,
            null,
            null,
            [],
            [],
            [error]);


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
    private static string SlotLabel(SlotKind kind, int position) =>
        kind == SlotKind.Primary ? "Primary" : $"Backup {position}";

    private static void ValidateRetentionSettings(int retentionDays, int batchSize)
    {
        if (retentionDays != VolunteerRetentionPolicy.MinimumRetentionDays)
        {
            throw new DomainException(
                $"Retention days must be exactly {VolunteerRetentionPolicy.MinimumRetentionDays}.");
        }

        if (batchSize is < VolunteerRetentionPolicy.MinimumBatchSize or > VolunteerRetentionPolicy.MaximumBatchSize)
        {
            throw new DomainException(
                $"Retention batch size must be between {VolunteerRetentionPolicy.MinimumBatchSize} and {VolunteerRetentionPolicy.MaximumBatchSize}.");
        }
    }

    private static int SlotOrder(ShiftSlot slot) => slot.Kind == SlotKind.Primary ? 0 : slot.Position;

    private static string Detail<T>(T value) => JsonSerializer.Serialize(value);
}
