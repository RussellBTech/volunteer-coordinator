using System.Text.Json;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Access;
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
    public const string StalePreviewMessage = "This information changed; review the updated details before confirming.";
    private const int VolunteerRetentionDays = VolunteerRetentionPolicy.MinimumRetentionDays;
    private const int MaxAssignmentLockAttempts = 3;
    private const string AssignmentLockConflictMessage = "The requested change conflicts with current schedule state. Reload and try again.";
    private readonly IWorkflowStore _store;
    private readonly IClock _clock;
    private readonly ITokenService _tokens;
    private readonly INotificationService _notifications;
    private readonly ITransientLinkMaterialStore? _transientLinkMaterial;
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
        INotificationService notifications,
        ITransientLinkMaterialStore? transientLinkMaterial = null)
    {
        _store = store;
        _clock = clock;
        _tokens = tokens;
        _notifications = notifications;
        _transientLinkMaterial = transientLinkMaterial;
    }

    public async Task<GroupSettingsDto?> GetGroupSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        return settings is null ? null : new GroupSettingsDto(settings.TimeZoneId, settings.Version);
    }
    public async Task<CoordinatorHomeDto> GetCoordinatorHomeAsync(CancellationToken cancellationToken)
    {
        var projection = await _store.GetCoordinatorHomeProjectionAsync(
            _clock.UtcNow,
            cancellationToken);

        if (projection.Settings is null)
        {
            return new CoordinatorHomeDto(
                true,
                BuildSetupSteps(projection, settingsConfigured: false),
                [],
                "Set your group time zone",
                "/Coordinator/Settings",
                null);
        }

        var settings = projection.Settings!;
        if (!projection.HasPublishedShift)
        {
            var (label, url) = projection.FirstUnpublishedShift is not null
                ? ("Review and publish the first schedule entry", $"/Coordinator/Schedule/Publish/{projection.FirstUnpublishedShift.Id}")
                : projection.FirstExpiredUnpublishedShift is not null
                    ? ("Edit the expired schedule entry", $"/Coordinator/Schedule/Edit/{projection.FirstExpiredUnpublishedShift.Id}")
                    : ("Create the first schedule entry", "/Coordinator/Schedule/Create");
            return new CoordinatorHomeDto(
                true,
                BuildSetupSteps(projection, settingsConfigured: true),
                [],
                label,
                url,
                settings.TimeZoneId);
        }

        var attention = new List<CoordinatorAttentionDto>(4);
        AddAttention(
            attention,
            "pending",
            "Requests to review",
            projection.PendingRequestCount,
            "/Coordinator/Requests?attention=pending",
            "Review requests",
            projection.PendingRequestExamples,
            settings);
        AddAttention(
            attention,
            "uncovered",
            "Open commitments",
            projection.UncoveredCommitmentCount,
            "/Coordinator/Coverage?attention=uncovered",
            "Open coverage",
            projection.UncoveredCommitmentExamples,
            settings);
        AddAttention(
            attention,
            "unconfirmed",
            "Waiting for confirmation",
            projection.UnconfirmedAssignmentCount,
            "/Coordinator/Coverage?attention=unconfirmed",
            "Review confirmations",
            projection.UnconfirmedAssignmentExamples,
            settings);
        AddAttention(
            attention,
            "message",
            "Messages not sent",
            projection.FailedMessageCount,
            "/Coordinator/Messages?attention=message",
            "Open messages",
            projection.FailedMessageExamples,
            settings);

        return new CoordinatorHomeDto(
            false,
            [],
            attention,
            null,
            null,
            settings.TimeZoneId);
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
            throw new DomainException("Select a supported group time zone.");
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
                        "Changing the group time zone keeps saved moments fixed and changes their displayed local dates and times. Confirm this consequence before saving.");
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
                var oldTitle = shift.Title;
                var oldLocation = shift.Location;
                var oldInstructions = shift.VolunteerInstructions;
                var oldStartsAtUtc = shift.StartsAtUtc;
                var oldEndsAtUtc = shift.EndsAtUtc;
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
                var visibleChanged =
                    !string.Equals(oldTitle, shift.Title, StringComparison.Ordinal) ||
                    !string.Equals(oldLocation, shift.Location, StringComparison.Ordinal) ||
                    !string.Equals(oldInstructions, shift.VolunteerInstructions, StringComparison.Ordinal) ||
                    oldStartsAtUtc != shift.StartsAtUtc ||
                    oldEndsAtUtc != shift.EndsAtUtc;
                if (visibleChanged)
                {
                    var activeAssignments = await _store.GetActiveAssignmentsAsync(
                        shift.Slots.Select(slot => slot.Id).ToArray(),
                        token);
                    foreach (var assignment in activeAssignments)
                    {
                        QueueNotification(
                            assignment.Id,
                            assignment.VolunteerId,
                            assignment.ShiftSlotId,
                            $"correction:{shift.Id:N}:{assignment.ShiftSlotId:N}:{assignment.VolunteerId:N}:{now.Ticks}",
                            "ScheduleCorrection",
                            now);
                    }

                    var pendingRequests = await _store.GetPendingRequestsAsync(
                        shift.Slots.Select(slot => slot.Id).ToArray(),
                        token);
                    foreach (var request in pendingRequests)
                    {
                        QueueNotification(
                            request.Id,
                            request.VolunteerId,
                            request.ShiftSlotId,
                            $"correction:{shift.Id:N}:{request.ShiftSlotId:N}:{request.VolunteerId:N}:{now.Ticks}",
                            "ScheduleCorrection",
                            now);
                    }
                }

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



    public Task DeactivateShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken) =>
        DeactivateShiftAsync(
            shiftId,
            expectedVersion,
            coordinatorEmail,
            cancellationToken,
            null,
            null);

    public async Task DeactivateShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken,
        string? expectedAffectedSet,
        uint? expectedSettingsVersion = null)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                await EnsureExpectedSettingsVersionAsync(expectedSettingsVersion, token);
                var shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException(StalePreviewMessage);
                }

                var slotIds = shift.Slots.Select(x => x.Id).ToArray();
                await LockSlotsAsync(slotIds, token);
                await _store.LockShiftAsync(shiftId, token);
                shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException(StalePreviewMessage);
                }

                var pendingRequests = await _store.GetPendingRequestsAsync(slotIds, token);
                var activeAssignments = await _store.GetActiveAssignmentsAsync(slotIds, token);
                if (expectedAffectedSet is not null &&
                    !string.Equals(
                        expectedAffectedSet,
                        BuildAffectedSet(
                            pendingRequests.Select(x => x.Id),
                            activeAssignments.Select(x => x.Id)),
                        StringComparison.Ordinal))
                {
                    throw new DomainException(StalePreviewMessage);
                }
                foreach (var request in pendingRequests)
                {
                    request.Supersede(actor, now);
                    await CancelPendingAccessIntentsAsync(
                        request.VolunteerId,
                        request.ShiftSlotId,
                        now,
                        token);
                    QueueNotification(
                        request.Id,
                        request.VolunteerId,
                        request.ShiftSlotId,
                        $"request:{request.Id:N}:deactivated",
                        "Deactivation",
                        now);
                    if (_store is IAccessStore accessStore)
                    {
                        foreach (var capability in await accessStore.GetActiveCapabilitiesAsync(
                                     request.VolunteerId,
                                     request.ShiftSlotId,
                                     token))
                        {
                            capability.Invalidate(now);
                        }

                        foreach (var recovery in await accessStore.GetRecoveryTokensForVolunteerAsync(
                                     request.VolunteerId,
                                     token))
                        {
                            if (recovery.ShiftSlotId == request.ShiftSlotId)
                            {
                                recovery.Invalidate(now);
                            }
                        }
                    }
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


    public Task PublishShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken) =>
        PublishShiftAsync(
            shiftId,
            expectedVersion,
            coordinatorEmail,
            cancellationToken,
            null,
            null);

    public async Task PublishShiftAsync(
        Guid shiftId,
        uint expectedVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken,
        string? expectedAffectedSet,
        uint? expectedSettingsVersion = null)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                await EnsureExpectedSettingsVersionAsync(expectedSettingsVersion, token);
                await _store.LockShiftAsync(shiftId, token);
                var shift = await RequireShiftAsync(shiftId, token);
                if (shift.Version != expectedVersion)
                {
                    throw new DomainException(StalePreviewMessage);
                }
                var activeAssignments = await _store.GetActiveAssignmentsAsync(
                    shift.Slots.Select(x => x.Id).ToArray(),
                    token);
                if (expectedAffectedSet is not null &&
                    !string.Equals(
                        expectedAffectedSet,
                        BuildAffectedSet([], activeAssignments.Select(x => x.Id)),
                        StringComparison.Ordinal))
                {
                    throw new DomainException(StalePreviewMessage);
                }

                shift.Publish(now);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "ShiftPublished", nameof(Shift), shift.Id, Detail(new { shift.PublishedAtUtc })));
                return true;
            },
            cancellationToken);
    }

    public async Task<CoordinatorActionPreviewDto> GetPublishPreviewAsync(
        Guid shiftId,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var shift = await RequireShiftAsync(shiftId, cancellationToken);
        if (!shift.IsActive || shift.PublishedAtUtc.HasValue)
        {
            throw new DomainException("This schedule entry is no longer waiting for publication.");
        }

        if (shift.EndsAtUtc <= _clock.UtcNow)
        {
            throw new DomainException("This schedule entry has ended. Edit it before reviewing publication.");
        }

        var slots = shift.Slots.Where(x => x.IsActive).OrderBy(SlotOrder).ToArray();
        var assignments = await _store.GetActiveAssignmentsAsync(
            slots.Select(x => x.Id).ToArray(),
            cancellationToken);
        var assignmentsBySlot = assignments.ToDictionary(x => x.ShiftSlotId);
        var volunteers = (await _store.GetVolunteersByIdsAsync(
                assignments.Select(x => x.VolunteerId).Distinct().ToArray(),
                cancellationToken))
            .ToDictionary(x => x.Id);
        var slotPreviews = slots
            .Select(slot =>
            {
                assignmentsBySlot.TryGetValue(slot.Id, out var assignment);
                volunteers.TryGetValue(assignment?.VolunteerId ?? Guid.Empty, out var volunteer);
                return new ConsequenceSlotDto(
                    SlotLabel(slot),
                    AssignmentStateLabel(assignment),
                    DisplayVolunteerName(volunteer),
                    BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            })
            .ToArray();

        var preview = new CoordinatorActionPreviewDto(
            "publish",
            shift.Id,
            shift.Id,
            null,
            shift.Version,
            null,
            null,
            null,
            BuildCommitment(shift, settings, null, "Schedule entry"),
            slotPreviews,
            [],
            null,
            null,
            [
                "The schedule entry will become public.",
                "Open commitments will be available for volunteers to request.",
                "The displayed local dates and slots will be visible to volunteers."
            ]);
        return preview with
        {
            ExpectedAffectedSet = BuildAffectedSet([], assignments.Select(x => x.Id)),
            ExpectedSettingsVersion = settings.Version
        };
    }

    public async Task<CoordinatorActionPreviewDto> GetDeactivatePreviewAsync(
        Guid shiftId,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var shift = await RequireShiftAsync(shiftId, cancellationToken);
        if (!shift.IsActive)
        {
            throw new DomainException("This schedule entry is already resolved.");
        }

        var slots = shift.Slots.Where(x => x.IsActive).OrderBy(SlotOrder).ToArray();
        var slotIds = slots.Select(x => x.Id).ToArray();
        var assignments = await _store.GetActiveAssignmentsAsync(slotIds, cancellationToken);
        var requests = await _store.GetPendingRequestsAsync(slotIds, cancellationToken);
        var volunteerIds = assignments.Select(x => x.VolunteerId)
            .Concat(requests.Select(x => x.VolunteerId))
            .Distinct()
            .ToArray();
        var volunteers = (await _store.GetVolunteersByIdsAsync(volunteerIds, cancellationToken))
            .ToDictionary(x => x.Id);
        var slotsById = slots.ToDictionary(x => x.Id);
        var people = new List<ConsequencePersonDto>(assignments.Count + requests.Count);
        foreach (var assignment in assignments)
        {
            if (slotsById.TryGetValue(assignment.ShiftSlotId, out var slot))
            {
                volunteers.TryGetValue(assignment.VolunteerId, out var volunteer);
                people.Add(new ConsequencePersonDto(
                    DisplayVolunteerName(volunteer) ?? "Removed volunteer",
                    SlotLabel(slot),
                    BuildCommitment(shift, settings, slot, SlotLabel(slot)),
                    "The assignment will be cancelled and its action links will stop working."));
            }
        }

        foreach (var request in requests)
        {
            if (slotsById.TryGetValue(request.ShiftSlotId, out var slot))
            {
                volunteers.TryGetValue(request.VolunteerId, out var volunteer);
                people.Add(new ConsequencePersonDto(
                    DisplayVolunteerName(volunteer) ?? "Removed volunteer",
                    SlotLabel(slot),
                    BuildCommitment(shift, settings, slot, SlotLabel(slot)),
                    "The request will be resolved without creating an assignment."));
            }
        }

        var preview = new CoordinatorActionPreviewDto(
            "deactivate",
            shift.Id,
            shift.Id,
            null,
            shift.Version,
            null,
            null,
            null,
            BuildCommitment(shift, settings, null, "Schedule entry"),
            [],
            people,
            null,
            null,
            [
                "The schedule entry will be removed from public openings.",
                $"{requests.Count} pending request(s) will be resolved.",
                $"{assignments.Count} active assignment(s) will be cancelled and their action links invalidated."
            ]);
        return preview with
        {
            ExpectedAffectedSet = BuildAffectedSet(
                requests.Select(x => x.Id),
                assignments.Select(x => x.Id)),
            ExpectedSettingsVersion = settings.Version
        };
    }

    public async Task<CoordinatorActionPreviewDto> GetCancelAssignmentPreviewAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var slotId = await _store.GetAssignmentSlotIdAsync(assignmentId, cancellationToken)
            ?? throw new DomainException("The assignment was not found.");
        var assignment = await RequireAssignmentAsync(assignmentId, cancellationToken);
        if (!assignment.IsActive)
        {
            throw new DomainException("Only an active assignment can be cancelled.");
        }

        var slot = await RequireSlotAsync(slotId, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        var volunteer = await RequireVolunteerAsync(assignment.VolunteerId, cancellationToken);
        var commitment = BuildCommitment(shift, settings, slot, SlotLabel(slot));
        return new CoordinatorActionPreviewDto(
            "cancel",
            assignment.Id,
            shift.Id,
            slot.Id,
            shift.Version,
            assignment.Id,
            assignment.VolunteerId,
            assignment.Status.ToString(),
            commitment,
            [],
            [
                new ConsequencePersonDto(
                    DisplayVolunteerName(volunteer) ?? "Removed volunteer",
                    SlotLabel(slot),
                    commitment,
                    "The assignment will end and its action links will stop working.")
            ],
            new PreviewVolunteerDto(
                DisplayVolunteerName(volunteer) ?? "Removed volunteer",
                volunteer.AnonymizedAtUtc.HasValue ? null : volunteer.Email),
            null,
            [
                "The commitment will become an open commitment.",
                "The volunteer's action links will stop working.",
                "The schedule entry and local date will remain unchanged."
            ],
            ExpectedSettingsVersion: settings.Version);
    }

    public async Task<CoordinatorActionPreviewDto> GetAssignmentPreviewAsync(
        Guid slotId,
        Guid? knownVolunteerId,
        string? volunteerName,
        string? volunteerEmail,
        string? volunteerPhone,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var slot = await RequireSlotAsync(slotId, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        if (!slot.IsActive || !shift.IsActive || shift.EndsAtUtc <= _clock.UtcNow)
        {
            throw new DomainException("This commitment is no longer available for assignment.");
        }

        var current = await _store.GetActiveAssignmentForSlotAsync(slotId, cancellationToken);
        var pendingRequests = await _store.GetPendingRequestsForSlotAsync(slotId, cancellationToken);
        Volunteer replacement;
        PreviewVolunteerDto replacementPreview;
        var replacementReusesExistingVolunteer = false;
        Guid? expectedSelectedVolunteerId;
        string expectedSelectedVolunteerNormalizedEmail;
        if (knownVolunteerId.HasValue)
        {
            replacement = await _store.GetVolunteerAsync(knownVolunteerId.Value, cancellationToken)
                ?? throw new DomainException("That volunteer is no longer available. Choose someone else.");
            if (replacement.AnonymizedAtUtc.HasValue)
            {
                throw new DomainException("That volunteer is no longer available. Choose someone else.");
            }

            expectedSelectedVolunteerId = replacement.Id;
            expectedSelectedVolunteerNormalizedEmail = replacement.NormalizedEmail;
            replacementPreview = new PreviewVolunteerDto(
                replacement.Name,
                replacement.Email,
                replacement.Phone);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(volunteerName) || string.IsNullOrWhiteSpace(volunteerEmail))
            {
                throw new DomainException("Enter the new volunteer's name and email before reviewing the assignment.");
            }

            var submittedVolunteer = Volunteer.Create(
                volunteerName,
                volunteerEmail,
                volunteerPhone,
                _clock.UtcNow);
            expectedSelectedVolunteerNormalizedEmail = submittedVolunteer.NormalizedEmail;
            var existingId = await _store.GetVolunteerIdByNormalizedEmailAsync(
                submittedVolunteer.NormalizedEmail,
                cancellationToken);
            if (existingId.HasValue)
            {
                replacement = await _store.GetVolunteerAsync(existingId.Value, cancellationToken)
                    ?? throw new DomainException("That volunteer is no longer available. Choose someone else.");
                if (replacement.AnonymizedAtUtc.HasValue)
                {
                    throw new DomainException("That volunteer is no longer available. Choose someone else.");
                }

                replacementReusesExistingVolunteer = true;
                expectedSelectedVolunteerId = replacement.Id;
                replacementPreview = new PreviewVolunteerDto(
                    submittedVolunteer.Name,
                    submittedVolunteer.Email,
                    submittedVolunteer.Phone);
            }
            else
            {
                replacement = submittedVolunteer;
                expectedSelectedVolunteerId = null;
                replacementPreview = new PreviewVolunteerDto(
                    replacement.Name,
                    replacement.Email,
                    replacement.Phone);
            }
        }

        var selectedVolunteerAssignment = await _store.GetActiveAssignmentForVolunteerAndShiftAsync(
            replacement.Id,
            shift.Id,
            cancellationToken);
        var assignmentIds = new[] { current, selectedVolunteerAssignment }
            .Where(x => x is not null)
            .Cast<Assignment>()
            .DistinctBy(x => x.Id)
            .ToArray();
        var volunteerIds = pendingRequests
            .Select(x => x.VolunteerId)
            .Concat(assignmentIds.Select(x => x.VolunteerId))
            .Distinct()
            .ToArray();
        var volunteers = (await _store.GetVolunteersByIdsAsync(volunteerIds, cancellationToken))
            .ToDictionary(x => x.Id);
        volunteers.TryGetValue(current?.VolunteerId ?? Guid.Empty, out var currentVolunteer);
        var commitment = BuildCommitment(shift, settings, slot, SlotLabel(slot));
        var actionKey = current is null ? "assign" : "replace";
        var people = new List<ConsequencePersonDto>(pendingRequests.Count + assignmentIds.Length);
        if (current is not null)
        {
            people.Add(
                new ConsequencePersonDto(
                    DisplayVolunteerName(currentVolunteer) ?? "Removed volunteer",
                    SlotLabel(slot),
                    commitment,
                    "The current assignment will be replaced and its action links will stop working.")
                {
                    AffectedAssignmentId = current.Id,
                    AffectedVolunteerId = current.VolunteerId
                });
        }

        if (selectedVolunteerAssignment is not null &&
            selectedVolunteerAssignment.Id != current?.Id &&
            shift.Slots.FirstOrDefault(x => x.Id == selectedVolunteerAssignment.ShiftSlotId) is { } otherSlot)
        {
            volunteers.TryGetValue(selectedVolunteerAssignment.VolunteerId, out var otherVolunteer);
            people.Add(
                new ConsequencePersonDto(
                    DisplayVolunteerName(otherVolunteer) ?? "Removed volunteer",
                    SlotLabel(otherSlot),
                    BuildCommitment(shift, settings, otherSlot, SlotLabel(otherSlot)),
                    "The selected volunteer's other assignment will be replaced so they keep one commitment in this schedule entry.")
                {
                    AffectedAssignmentId = selectedVolunteerAssignment.Id,
                    AffectedVolunteerId = selectedVolunteerAssignment.VolunteerId
                });
        }

        foreach (var request in pendingRequests)
        {
            volunteers.TryGetValue(request.VolunteerId, out var requestVolunteer);
            people.Add(
                new ConsequencePersonDto(
                    DisplayVolunteerName(requestVolunteer) ?? "Removed volunteer",
                    SlotLabel(slot),
                    commitment,
                    "This pending request will be resolved without creating another assignment.")
                {
                    AffectedRequestId = request.Id,
                    AffectedVolunteerId = request.VolunteerId
                });
        }

        var consequences = new List<string>();
        if (current is not null)
        {
            consequences.Add("The current volunteer's action links will stop working.");
        }

        if (replacementReusesExistingVolunteer)
        {
            consequences.Add("The existing volunteer record with this email will be updated and reused.");
        }
        else if (!knownVolunteerId.HasValue)
        {
            consequences.Add("A new volunteer record will be created with these contact details.");
        }

        consequences.Add(
            current is null
                ? "The selected volunteer will be assigned to this open commitment."
                : "The selected volunteer will replace the current assignment.");
        if (selectedVolunteerAssignment is not null &&
            selectedVolunteerAssignment.Id != current?.Id)
        {
            consequences.Add("The selected volunteer's other assignment in this schedule entry will be replaced.");
        }

        if (pendingRequests.Count > 0)
        {
            consequences.Add(
                $"{pendingRequests.Count} pending request(s) on this commitment will be resolved without creating another assignment.");
        }

        consequences.Add("The volunteer will receive a commitment message when delivery is available.");
        var targetSlotPreview = new ConsequenceSlotDto(
            SlotLabel(slot),
            AssignmentStateLabel(current),
            DisplayVolunteerName(currentVolunteer),
            commitment);
        var currentPreview = current is null
            ? null
            : new PreviewVolunteerDto(
                DisplayVolunteerName(currentVolunteer) ?? "Removed volunteer",
                currentVolunteer is not null && !currentVolunteer.AnonymizedAtUtc.HasValue ? currentVolunteer.Email : null,
                currentVolunteer is not null && !currentVolunteer.AnonymizedAtUtc.HasValue ? currentVolunteer.Phone : null);
        var preview = new CoordinatorActionPreviewDto(
            actionKey,
            slot.Id,
            shift.Id,
            slot.Id,
            shift.Version,
            current?.Id,
            current?.VolunteerId,
            current?.Status.ToString(),
            commitment,
            [targetSlotPreview],
            people,
            currentPreview,
            replacementPreview,
            consequences,
            ReplacementReusesExistingVolunteer: replacementReusesExistingVolunteer,
            ExpectedSettingsVersion: settings.Version);
        return preview with
        {
            ExpectedAffectedSet = BuildAssignmentAffectedSet(
                pendingRequests.Select(x => x.Id),
                assignmentIds.Select(x => x.Id),
                expectedSelectedVolunteerId),
            ExpectedSelectedVolunteerId = expectedSelectedVolunteerId,
            ExpectedSelectedVolunteerNormalizedEmail = expectedSelectedVolunteerNormalizedEmail
        };
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
        NotificationIntent? requestIntent = null;
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

                var request = ShiftRequest.Create(slot.Id, volunteer.Id, now);
                _store.AddRequest(request);
                if (_store is IAccessStore accessStore)
                {
                    foreach (var previous in await accessStore.GetActiveCapabilitiesAsync(volunteer.Id, slot.Id, token))
                    {
                        previous.Invalidate(now);
                    }

                    accessStore.AddCapability(VolunteerAccessCapability.Create(
                        slot.Id,
                        volunteer.Id,
                        generatedToken.Hash,
                        now,
                        CapabilityIssuedReason.Request));
                }

                requestIntent = QueueNotification(
                    request.Id,
                    volunteer.Id,
                    slot.Id,
                    $"request:{request.Id:N}:receipt",
                    "RequestReceipt",
                    now);
                _store.AddAuditEntry(AuditEntry.Create(now, $"volunteer:{volunteer.Id}", "RequestSubmitted", nameof(ShiftRequest), request.Id, Detail(new { request.ShiftSlotId, request.VolunteerId })));
                return (request.Id, VolunteerId: volunteer.Id, Commitment: BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            },
            cancellationToken);
        if (requestIntent is not null)
        {
            _transientLinkMaterial?.PutHubToken(
                requestIntent.Id,
                generatedToken.RawToken,
                now.AddMinutes(15));
        }


        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "RequestReceived", result.VolunteerId), cancellationToken);
        return new RequestSubmission(result.Id, generatedToken.RawToken, notification.Warning, result.Commitment);
    }

    public async Task<RequestStatusDto> GetRequestStatusAsync(
        string rawStatusToken,
        CancellationToken cancellationToken)
    {
        var hub = await InspectCommitmentHubAsync(rawStatusToken, cancellationToken);
        return new RequestStatusDto(
            hub.RequestId,
            hub.VolunteerName,
            hub.Commitment,
            hub.RequestStatus,
            hub.AssignmentStatus);
    }

    public async Task<CommitmentHubDto> InspectCommitmentHubAsync(
        string rawCapability,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        if (_store is not IAccessStore accessStore)
        {
            throw InvalidHub();
        }

        var hash = HashRequiredToken(rawCapability);
        var slotId = await accessStore.GetCapabilitySlotIdByHashAsync(hash, cancellationToken);
        if (!slotId.HasValue)
        {
            throw InvalidHub();
        }

        var capability = await accessStore.GetCapabilityByHashAsync(hash, cancellationToken);
        if (capability is null || !_tokens.FixedTimeEquals(hash, capability.TokenHash))
        {
            throw InvalidHub();
        }

        var slot = await RequireSlotAsync(slotId.Value, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        var volunteer = await RequireVolunteerAsync(capability.VolunteerId, cancellationToken);
        if (!capability.IsUsable(
                _clock.UtcNow,
                shift.EndsAtUtc,
                slot.IsActive,
                shift.IsActive,
                volunteer.AnonymizedAtUtc.HasValue))
        {
            throw InvalidHub();
        }

        var request = await accessStore.GetRequestForVolunteerSlotAsync(
            capability.VolunteerId,
            slot.Id,
            cancellationToken);
        var assignment = await FindLatestAssignmentAsync(
            capability.VolunteerId,
            slot.Id,
            cancellationToken);
        var requestId = request?.Id ?? assignment?.SourceRequestId ?? Guid.Empty;
        var requestStatus = request?.Status.ToString() ?? "None";
        var assignmentStatus = assignment?.Status.ToString();
        var offered = OfferedHubActions(assignment, shift, _clock.UtcNow);
        return new CommitmentHubDto(
            capability.Id,
            requestId,
            volunteer.Name,
            BuildCommitment(shift, settings, slot, SlotLabel(slot)),
            requestStatus,
            assignmentStatus,
            offered,
            HubStatusMessage(requestStatus, assignmentStatus, offered, shift, _clock.UtcNow));
    }

    public async Task<CommandResult<string>> ApplyHubActionAsync(
        string rawCapability,
        string action,
        CancellationToken cancellationToken)
    {
        if (_store is not IAccessStore accessStore)
        {
            throw InvalidHub();
        }

        var normalizedAction = action.Trim();
        if (normalizedAction is not ("Confirm" or "Decline" or "Cancel"))
        {
            throw InvalidHub();
        }

        var hash = HashRequiredToken(rawCapability);
        var now = _clock.UtcNow;
        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var slotId = await accessStore.GetCapabilitySlotIdByHashAsync(hash, token);
                if (!slotId.HasValue)
                {
                    throw InvalidHub();
                }

                await _store.LockSlotAsync(slotId.Value, token);
                var capability = await accessStore.GetCapabilityByHashAsync(hash, token);
                if (capability is null || !_tokens.FixedTimeEquals(hash, capability.TokenHash))
                {
                    throw InvalidHub();
                }

                var slot = await RequireSlotAsync(slotId.Value, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                var volunteer = await RequireVolunteerAsync(capability.VolunteerId, token);
                if (!capability.IsUsable(
                        now,
                        shift.EndsAtUtc,
                        slot.IsActive,
                        shift.IsActive,
                        volunteer.AnonymizedAtUtc.HasValue))
                {
                    throw InvalidHub();
                }

                var assignment = await FindLatestAssignmentAsync(
                    capability.VolunteerId,
                    slot.Id,
                    token);
                if (assignment is null || !OfferedHubActions(assignment, shift, now).Contains(normalizedAction, StringComparer.Ordinal))
                {
                    throw InvalidHub();
                }

                switch (normalizedAction)
                {
                    case "Confirm":
                        assignment.Confirm(now);
                        break;
                    case "Decline":
                        assignment.Decline(now);
                        break;
                    case "Cancel":
                        assignment.Cancel(now);
                        break;
                }

                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    "volunteer-token",
                    $"Assignment{normalizedAction}",
                    nameof(VolunteerAccessCapability),
                    capability.Id,
                    Detail(new { assignment.Status, assignment.ShiftSlotId })));
                QueueNotification(
                    assignment.Id,
                    volunteer.Id,
                    slot.Id,
                    $"action:{assignment.Id:N}:{normalizedAction}:{assignment.Status}",
                    $"Volunteer{normalizedAction}",
                    now);
                return (Action: normalizedAction, VolunteerId: volunteer.Id);
            },
            cancellationToken);
        var notification = await NotifySafelyAsync(
            new NotificationMessage(Guid.NewGuid(), $"Assignment{result.Action}", result.VolunteerId),
            cancellationToken);
        return new CommandResult<string>(result.Action, notification.Warning);
    }

    public async Task<RecoveryRequestResult> RequestRecoveryAsync(
        string email,
        DateOnly commitmentDate,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var normalizedEmail = Volunteer.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new DomainException("Enter a valid email address.");
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var localStart = commitmentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone));
        var endUtc = startUtc.AddDays(1);
        if (_store is IAccessStore accessStore && _store is INotificationOutboxStore outbox)
        {
            var matches = await FindRecoveryMatchesAsync(
                normalizedEmail,
                startUtc,
                endUtc,
                cancellationToken);
            foreach (var match in matches.Take(3))
            {
                var eventKey = $"recovery:{match.VolunteerId:N}:{match.SlotId:N}:{commitmentDate:yyyyMMdd}";
                if (await outbox.HasEquivalentPendingIntentAsync(eventKey, cancellationToken))
                {
                    continue;
                }

                try
                {
                    await _store.ExecuteInTransactionAsync(
                        async token =>
                        {
                            await _store.LockSlotAsync(match.SlotId, token);
                            if (await outbox.HasEquivalentPendingIntentAsync(eventKey, token))
                            {
                                return true;
                            }

                            if (await _store.GetVolunteerAsync(match.VolunteerId, token) is not { AnonymizedAtUtc: null })
                            {
                                return true;
                            }

                            var currentRequest = (await accessStore.GetRequestsForVolunteerSlotAsync(
                                    match.VolunteerId,
                                    match.SlotId,
                                    token))
                                .OrderByDescending(x => x.RequestedAtUtc)
                                .ThenByDescending(x => x.Id)
                                .FirstOrDefault();
                            var currentAssignment = (await _store.GetAssignmentsForVolunteerAsync(
                                    match.VolunteerId,
                                    token))
                                .Where(x => x.ShiftSlotId == match.SlotId && x.IsActive)
                                .OrderByDescending(x => x.AssignedAtUtc)
                                .ThenByDescending(x => x.Id)
                                .FirstOrDefault();
                            if (currentAssignment is null && currentRequest?.Status != RequestStatus.Pending)
                            {
                                return true;
                            }

                            QueueNotification(
                                Guid.NewGuid(),
                                match.VolunteerId,
                                match.SlotId,
                                eventKey,
                                "AccessRecovery",
                                _clock.UtcNow);
                            return true;
                        },
                        cancellationToken);
                }
                catch (DomainException)
                {
                    // A concurrent equivalent enqueue has the same generic caller outcome.
                }
            }
        }

        return new RecoveryRequestResult(
            "If those details match an eligible commitment, a recovery email will arrive shortly. The current link remains valid until replacement access is completed.");
    }

    public async Task<string> RedeemRecoveryAsync(
        string rawRecoveryToken,
        CancellationToken cancellationToken)
    {
        if (_store is not IAccessStore accessStore)
        {
            throw InvalidRecovery();
        }

        var hash = HashRequiredToken(rawRecoveryToken);
        var now = _clock.UtcNow;
        var result = await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var slotId = await accessStore.GetRecoverySlotIdByHashAsync(hash, token);
                if (!slotId.HasValue)
                {
                    throw InvalidRecovery();
                }

                await _store.LockSlotAsync(slotId.Value, token);
                var recovery = await accessStore.GetRecoveryTokenByHashAsync(hash, token);
                if (recovery is null || !_tokens.FixedTimeEquals(hash, recovery.TokenHash) || !recovery.IsUsable(now))
                {
                    throw InvalidRecovery();
                }

                var slot = await RequireSlotAsync(slotId.Value, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                var volunteer = await RequireVolunteerAsync(recovery.VolunteerId, token);
                if (!slot.IsActive || !shift.IsActive || volunteer.AnonymizedAtUtc.HasValue || now > shift.EndsAtUtc.AddDays(7))
                {
                    throw InvalidRecovery();
                }

                var latestRequest = (await accessStore.GetRequestsForVolunteerSlotAsync(
                        recovery.VolunteerId,
                        slot.Id,
                        token))
                    .OrderByDescending(x => x.RequestedAtUtc)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefault();
                var currentAssignment = (await _store.GetAssignmentsForVolunteerAsync(
                        recovery.VolunteerId,
                        token))
                    .Where(x => x.ShiftSlotId == slot.Id && x.IsActive)
                    .OrderByDescending(x => x.AssignedAtUtc)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefault();
                if (currentAssignment is null && latestRequest?.Status != RequestStatus.Pending)
                {
                    throw InvalidRecovery();
                }

                recovery.Consume(now);
                foreach (var capability in await accessStore.GetActiveCapabilitiesAsync(
                             recovery.VolunteerId,
                             slot.Id,
                             token))
                {
                    capability.Invalidate(now);
                }

                var generated = _tokens.Generate();
                accessStore.AddCapability(VolunteerAccessCapability.Create(
                    slot.Id,
                    recovery.VolunteerId,
                    generated.Hash,
                    now,
                    CapabilityIssuedReason.Recovery));
                QueueNotification(
                    Guid.NewGuid(),
                    volunteer.Id,
                    slot.Id,
                    $"recovery:{recovery.Id:N}:redeemed",
                    "RecoveryRedeemed",
                    now);
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    "volunteer-recovery",
                    "VolunteerAccessRecovered",
                    nameof(RecoveryToken),
                    recovery.Id,
                    Detail(new { recovery.VolunteerId, recovery.ShiftSlotId })));
                return generated.RawToken;
            },
            cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<CoordinatorRequestDto>> ListRequestsAsync(
        CancellationToken cancellationToken)
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

                QueueNotification(
                    assignment.Id,
                    volunteer.Id,
                    slot.Id,
                    $"assignment:{assignment.Id:N}:access",
                    "AssignmentAccess",
                    now);
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
                QueueNotification(
                    request.Id,
                    volunteer.Id,
                    request.ShiftSlotId,
                    $"request:{request.Id:N}:rejected",
                    "RequestDecision",
                    now);
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

                QueueNotification(
                    assignment.Id,
                    volunteer.Id,
                    slot.Id,
                    $"assignment:{assignment.Id:N}:access",
                    "AssignmentAccess",
                    now);
                await SupersedeOtherRequestsAsync(slot.Id, null, actor, now, token);
                _store.AddAuditEntry(AuditEntry.Create(now, actor, "AssignmentCreatedOrReassigned", nameof(Assignment), assignment.Id, Detail(new { assignment.ShiftSlotId, assignment.VolunteerId })));
                return (assignment.Id, VolunteerId: volunteer.Id, Commitment: BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            },
            cancellationToken);
        var notification = await NotifySafelyAsync(new NotificationMessage(result.Id, "AssignmentCreated", result.VolunteerId), cancellationToken);
        return new AssignmentResult(result.Id, notification.Warning, result.Commitment);
    }
    public async Task<AssignmentResult> AssignVolunteerAsync(
        Guid slotId,
        Guid? expectedAssignmentId,
        Guid? expectedVolunteerId,
        string? expectedAssignmentState,
        uint expectedShiftVersion,
        Guid? knownVolunteerId,
        string? volunteerName,
        string? volunteerEmail,
        string? volunteerPhone,
        string coordinatorEmail,
        CancellationToken cancellationToken,
        uint? expectedSettingsVersion = null,
        string? expectedAffectedSet = null,
        Guid? expectedSelectedVolunteerId = null,
        string? expectedSelectedVolunteerNormalizedEmail = null)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var result = await ExecuteWithAssignmentLockRetryAsync(
            async token =>
            {
                await EnsureExpectedSettingsVersionAsync(expectedSettingsVersion, token);
                var preflightSlot = await RequireSlotAsync(slotId, token);
                var preflightShift = await RequireShiftAsync(preflightSlot.ShiftId, token);
                var preflightVolunteerId = knownVolunteerId;
                if (!preflightVolunteerId.HasValue && !string.IsNullOrWhiteSpace(volunteerEmail))
                {
                    preflightVolunteerId = await _store.GetVolunteerIdByNormalizedEmailAsync(
                        Volunteer.NormalizeEmail(volunteerEmail),
                        token);
                }
                var normalizedVolunteerEmail = string.IsNullOrWhiteSpace(volunteerEmail)
                    ? null
                    : Volunteer.NormalizeEmail(volunteerEmail);
                var selectionIdIsBound = expectedSelectedVolunteerId.HasValue || knownVolunteerId.HasValue;
                if (expectedAffectedSet is not null &&
                    selectionIdIsBound &&
                    preflightVolunteerId != (expectedSelectedVolunteerId ?? knownVolunteerId))
                {
                    throw new DomainException(StalePreviewMessage);
                }

                var lockedAssignmentSlots = await LockAssignmentSlotsAsync(
                    slotId,
                    preflightShift.Id,
                    preflightVolunteerId,
                    token);
                if (preflightVolunteerId.HasValue)
                {
                    await _store.LockVolunteerAsync(preflightVolunteerId.Value, token);
                }

                await _store.LockShiftAsync(preflightShift.Id, token);
                var slot = await RequireSlotAsync(slotId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                var settings = await _store.GetGroupSettingsAsync(token)
                    ?? throw new DomainException(CommitmentUnavailableMessage);
                var current = await _store.GetActiveAssignmentForSlotAsync(slotId, token);
                if (!slot.IsActive ||
                    !shift.IsActive ||
                    shift.EndsAtUtc <= now ||
                    shift.Version != expectedShiftVersion ||
                    !MatchesExpectedAssignment(current, expectedAssignmentId, expectedVolunteerId, expectedAssignmentState))
                {
                    throw new DomainException(StalePreviewMessage);
                }

                var actualSelectedVolunteerId = knownVolunteerId ?? (normalizedVolunteerEmail is null
                    ? null
                    : await _store.GetVolunteerIdByNormalizedEmailAsync(normalizedVolunteerEmail, token));
                if (expectedAffectedSet is not null &&
                    selectionIdIsBound &&
                    actualSelectedVolunteerId != (expectedSelectedVolunteerId ?? knownVolunteerId))
                {
                    throw new DomainException(StalePreviewMessage);
                }

                if (expectedAffectedSet is not null &&
                    !knownVolunteerId.HasValue &&
                    ((expectedSelectedVolunteerId.HasValue && actualSelectedVolunteerId is null) ||
                     !string.Equals(
                         normalizedVolunteerEmail ?? string.Empty,
                         expectedSelectedVolunteerNormalizedEmail ?? string.Empty,
                         StringComparison.Ordinal)))
                {
                    throw new DomainException(StalePreviewMessage);
                }

                var selectedVolunteerAssignment = actualSelectedVolunteerId.HasValue
                    ? await _store.GetActiveAssignmentForVolunteerAndShiftAsync(
                        actualSelectedVolunteerId.Value,
                        shift.Id,
                        token)
                    : null;
                var pendingRequests = await _store.GetPendingRequestsForSlotAsync(slotId, token);
                var actualAffectedSet = BuildAssignmentAffectedSet(
                    pendingRequests.Select(x => x.Id),
                    new[] { current, selectedVolunteerAssignment }
                        .Where(x => x is not null)
                        .Cast<Assignment>()
                        .DistinctBy(x => x.Id)
                        .Select(x => x.Id),
                    actualSelectedVolunteerId);
                if (expectedAffectedSet is not null &&
                    !string.Equals(expectedAffectedSet, actualAffectedSet, StringComparison.Ordinal))
                {
                    throw new DomainException(StalePreviewMessage);
                }

                Volunteer volunteer;
                if (knownVolunteerId.HasValue)
                {
                    volunteer = await _store.GetVolunteerAsync(knownVolunteerId.Value, token)
                        ?? throw new DomainException("That volunteer is no longer available. Choose someone else.");
                    if (volunteer.AnonymizedAtUtc.HasValue)
                    {
                        throw new DomainException("That volunteer is no longer available. Choose someone else.");
                    }

                    if (expectedAffectedSet is not null &&
                        (actualSelectedVolunteerId != volunteer.Id ||
                         (expectedSelectedVolunteerNormalizedEmail is not null &&
                          !string.Equals(
                              volunteer.NormalizedEmail,
                              expectedSelectedVolunteerNormalizedEmail,
                              StringComparison.Ordinal))))
                    {
                        throw new DomainException(StalePreviewMessage);
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(volunteerName) ||
                        string.IsNullOrWhiteSpace(volunteerEmail))
                    {
                        throw new DomainException("Enter the new volunteer's name and email.");
                    }

                    var volunteerId = actualSelectedVolunteerId;
                    if (volunteerId.HasValue)
                    {
                        if (!preflightVolunteerId.HasValue || volunteerId.Value != preflightVolunteerId.Value)
                        {
                            await _store.LockVolunteerAsync(volunteerId.Value, token);
                        }

                        volunteer = await RequireVolunteerAsync(volunteerId.Value, token);
                        if (volunteer.AnonymizedAtUtc.HasValue)
                        {
                            throw new DomainException("That volunteer is no longer available. Choose someone else.");
                        }

                        if (expectedAffectedSet is not null &&
                            (actualSelectedVolunteerId != volunteer.Id ||
                             (expectedSelectedVolunteerNormalizedEmail is not null &&
                              !string.Equals(
                                  volunteer.NormalizedEmail,
                                  expectedSelectedVolunteerNormalizedEmail,
                                  StringComparison.Ordinal))))
                        {
                            throw new DomainException(StalePreviewMessage);
                        }

                        volunteer.UpdateContact(volunteerName, volunteerEmail, volunteerPhone, now);
                    }
                    else
                    {
                        if (expectedAffectedSet is not null && expectedSelectedVolunteerId.HasValue)
                        {
                            throw new DomainException(StalePreviewMessage);
                        }

                        volunteer = Volunteer.Create(volunteerName, volunteerEmail, volunteerPhone, now);
                        _store.AddVolunteer(volunteer);
                    }
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

                QueueNotification(
                    assignment.Id,
                    volunteer.Id,
                    slot.Id,
                    $"assignment:{assignment.Id:N}:access",
                    "AssignmentAccess",
                    now);
                await SupersedeOtherRequestsAsync(slot.Id, null, actor, now, token);
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "AssignmentCreatedOrReassigned",
                    nameof(Assignment),
                    assignment.Id,
                    Detail(new { assignment.ShiftSlotId, assignment.VolunteerId })));
                return (assignment.Id, VolunteerId: volunteer.Id, Commitment: BuildCommitment(shift, settings, slot, SlotLabel(slot)));
            },
            cancellationToken);

        var notification = await NotifySafelyAsync(
            new NotificationMessage(result.Id, "AssignmentCreated", result.VolunteerId),
            cancellationToken);
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
    public async Task CancelAssignmentAsync(
        Guid assignmentId,
        uint expectedShiftVersion,
        Guid? expectedVolunteerId,
        string? expectedAssignmentState,
        string coordinatorEmail,
        CancellationToken cancellationToken,
        uint? expectedSettingsVersion = null)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                await EnsureExpectedSettingsVersionAsync(expectedSettingsVersion, token);
                var slotId = await _store.GetAssignmentSlotIdAsync(assignmentId, token)
                    ?? throw new DomainException("The assignment was not found.");
                await _store.LockSlotAsync(slotId, token);
                var slot = await RequireSlotAsync(slotId, token);
                await _store.LockShiftAsync(slot.ShiftId, token);
                var shift = await RequireShiftAsync(slot.ShiftId, token);
                var assignment = await RequireAssignmentAsync(assignmentId, token);
                if (shift.Version != expectedShiftVersion ||
                    !MatchesExpectedAssignment(
                        assignment,
                        assignmentId,
                        expectedVolunteerId,
                        expectedAssignmentState) ||
                    !assignment.IsActive)
                {
                    throw new DomainException(StalePreviewMessage);
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

    public async Task RequestAccessReissueAsync(
        Guid assignmentId,
        bool revokeNow,
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
                    throw new DomainException("Only an active assignment can receive replacement access.");
                }

                var volunteer = await RequireVolunteerAsync(assignment.VolunteerId, token);
                if (volunteer.AnonymizedAtUtc.HasValue)
                {
                    throw new DomainException("Removed volunteer contact data cannot receive access.");
                }

                if (revokeNow && _store is IAccessStore accessStore)
                {
                    foreach (var capability in await accessStore.GetActiveCapabilitiesAsync(
                                 assignment.VolunteerId,
                                 assignment.ShiftSlotId,
                                 token))
                    {
                        capability.Invalidate(now);
                    }

                    foreach (var recovery in await accessStore.GetRecoveryTokensForVolunteerAsync(
                                 assignment.VolunteerId,
                                 token))
                    {
                        if (recovery.ShiftSlotId == assignment.ShiftSlotId)
                        {
                            recovery.Invalidate(now);
                        }
                    }

                    await CancelPendingAccessIntentsAsync(
                        assignment.VolunteerId,
                        assignment.ShiftSlotId,
                        now,
                        token);

                    _store.AddAuditEntry(AuditEntry.Create(
                        now,
                        actor,
                        "VolunteerAccessRevokedByCoordinator",
                        nameof(Assignment),
                        assignment.Id,
                        Detail(new { assignment.ShiftSlotId, assignment.VolunteerId })));
                }

                var mode = revokeNow ? "RevokeNow" : "Normal";
                var eventKey = $"reissue:{assignment.Id:N}:{mode}";
                NotificationIntent? notificationIntent = null;
                if (_store is INotificationOutboxStore outbox)
                {
                    notificationIntent = await outbox.GetEquivalentPendingIntentAsync(
                        eventKey,
                        token);
                    notificationIntent ??= QueueNotification(
                        assignment.Id,
                        volunteer.Id,
                        assignment.ShiftSlotId,
                        eventKey,
                        "CoordinatorAccessReissue",
                        now);
                }
                else
                {
                    notificationIntent = QueueNotification(
                        assignment.Id,
                        volunteer.Id,
                        assignment.ShiftSlotId,
                        eventKey,
                        "CoordinatorAccessReissue",
                        now);
                }

                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "VolunteerAccessReissueRequested",
                    nameof(Assignment),
                    assignment.Id,
                    Detail(new
                    {
                        assignment.ShiftSlotId,
                        assignment.VolunteerId,
                        Mode = mode,
                        NotificationCorrelationId = notificationIntent?.Id
                    })));
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
    public async Task<IReadOnlyList<VolunteerDto>> ListEligibleVolunteersAsync(
        CancellationToken cancellationToken)
    {
        var volunteers = await _store.GetVolunteersAsync(cancellationToken);
        return volunteers
            .Where(x => !x.AnonymizedAtUtc.HasValue)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.NormalizedEmail)
            .Select(x => new VolunteerDto(x.Id, x.Name, x.Email, x.Phone))
            .ToArray();
    }

    public async Task<IReadOnlyList<CoordinatorMessageDto>> ListActionableMessagesAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return [];
        }

        var now = _clock.UtcNow;
        var examples = await _store.GetActionableMessageExamplesAsync(
            now,
            200,
            cancellationToken);
        return examples
            .Select(example => new CoordinatorMessageDto(
                example.VolunteerName ?? "Removed volunteer",
                BuildCommitment(example, settings),
                example.OccurredAtUtc ?? now,
                MessagePurpose(example.MessageKind) ?? "Commitment update",
                "Message could not be sent. Contact the volunteer another way.",
                example.VolunteerEmail,
                example.VolunteerPhone))
            .ToArray();
    }

    public async Task<CoordinatorMessagePageDto> GetActionableMessagesPageAsync(
        int page,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return new CoordinatorMessagePageDto(1, 50, 0, []);
        }

        var now = _clock.UtcNow;
        var projection = await _store.GetActionableMessagePageAsync(
            now,
            page,
            50,
            cancellationToken);
        var messages = projection.Items
            .Select(example => new CoordinatorMessageDto(
                example.VolunteerName ?? "Removed volunteer",
                BuildCommitment(example, settings),
                example.OccurredAtUtc ?? now,
                MessagePurpose(example.MessageKind) ?? "Commitment update",
                "Message could not be sent. Contact the volunteer another way.",
                example.VolunteerEmail,
                example.VolunteerPhone))
            .ToArray();
        return new CoordinatorMessagePageDto(
            projection.Page,
            projection.PageSize,
            projection.TotalCount,
            messages);
    }

    public async Task<IReadOnlyList<NotificationIntentDto>> ListNotificationIntentsAsync(
        CancellationToken cancellationToken)
    {
        if (_store is not INotificationOutboxStore outbox)
        {
            return [];
        }

        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return [];
        }

        var intents = await outbox.GetNotificationIntentsAsync(500, cancellationToken);
        var attempts = (await outbox.GetDeliveryAttemptsAsync(
                intents.Select(x => x.Id).ToArray(),
                cancellationToken))
            .GroupBy(x => x.NotificationIntentId)
            .ToDictionary(x => x.Key, x => x
                .OrderBy(attempt => attempt.Ordinal)
                .Select(attempt => new NotificationAttemptDto(
                    attempt.Ordinal,
                    attempt.StartedAtUtc,
                    attempt.CompletedAtUtc,
                    attempt.OutcomeCategory))
                .ToArray());
        var volunteers = (await _store.GetVolunteersByIdsAsync(
                intents.Select(x => x.VolunteerId).Distinct().ToArray(),
                cancellationToken))
            .ToDictionary(x => x.Id);
        var result = new List<NotificationIntentDto>(intents.Count);
        foreach (var intent in intents)
        {
            volunteers.TryGetValue(intent.VolunteerId, out var volunteer);
            CommitmentDto? commitment = null;
            Guid? accessAssignmentId = null;
            if (intent.ShiftSlotId is Guid slotId &&
                await _store.GetSlotAsync(slotId, cancellationToken) is { } slot &&
                await _store.GetShiftAsync(slot.ShiftId, cancellationToken) is { } shift)
            {
                commitment = BuildCommitment(
                    shift,
                    settings,
                    slot,
                    SlotLabel(slot));
                if (IsLinkBearingNotificationKind(intent.Kind))
                {
                    var slotAssignment = await _store.GetActiveAssignmentForSlotAsync(
                        slot.Id,
                        cancellationToken);
                    if (slotAssignment?.VolunteerId == intent.VolunteerId)
                    {
                        accessAssignmentId = slotAssignment.Id;
                    }
                }
            }

            result.Add(new NotificationIntentDto(
                intent.Id,
                intent.VolunteerId,
                volunteer?.AnonymizedAtUtc.HasValue == true ? "Removed volunteer" : volunteer?.Name ?? "Removed volunteer",
                commitment,
                intent.Kind,
                intent.State.ToString(),
                intent.CreatedAtUtc,
                intent.NextAttemptAtUtc,
                intent.AttemptCount,
                intent.FailureCategory,
                attempts.TryGetValue(intent.Id, out var rows) ? rows : [],
                accessAssignmentId));
        }

        return result;
    }

    public async Task RequestNotificationResendAsync(
        Guid intentId,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        if (_store is not INotificationOutboxStore outbox)
        {
            throw new DomainException("Transactional messaging is unavailable.");
        }

        await _store.ExecuteInTransactionAsync(
            async token =>
            {
                var intent = await outbox.GetNotificationIntentForUpdateAsync(
                        intentId,
                        token)
                    ?? throw new DomainException("The message was not found.");
                if (IsLinkBearingNotificationKind(intent.Kind))
                {
                    throw new DomainException(
                        "Access messages must use replacement access controls.");
                }

                if (intent.State is NotificationIntentState.Pending or
                    NotificationIntentState.RetryScheduled or
                    NotificationIntentState.InFlight)
                {
                    throw new DomainException("This message is already waiting for delivery.");
                }

                var eventKey = $"resend:{intent.Id:N}";
                if (await outbox.GetEquivalentPendingIntentAsync(eventKey, token) is not null)
                {
                    throw new DomainException("Another copy is already waiting for delivery.");
                }

                var now = _clock.UtcNow;
                QueueNotification(
                    intent.TransitionId,
                    intent.VolunteerId,
                    intent.ShiftSlotId,
                    eventKey,
                    intent.Kind,
                    now);

                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "NotificationResendRequested",
                    nameof(NotificationIntent),
                    intent.Id,
                    Detail(new { intent.VolunteerId, intent.ShiftSlotId, intent.Kind })));
                return true;
            },
            cancellationToken);
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

    public async Task<ActionInspectionDto> InspectActionAsync(string rawToken, CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken)
            ?? throw new DomainException(CommitmentUnavailableMessage);
        var (token, assignment) = await ResolveActionAsync(rawToken, cancellationToken);
        var slot = await RequireSlotAsync(assignment.ShiftSlotId, cancellationToken);
        var shift = await RequireShiftAsync(slot.ShiftId, cancellationToken);
        var volunteer = await RequireVolunteerAsync(assignment.VolunteerId, cancellationToken);
        var canApply = CanApply(token.Action, assignment.Status) &&
            LegacyActionDeadlineOpen(token.Action, shift, _clock.UtcNow);
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
        if (_store is not ILegacyActionTokenStore legacyStore)
        {
            throw new DomainException("This action link is invalid, expired, or already used.");
        }
        var hash = HashRequiredToken(rawToken);
        var result = await _store.ExecuteInTransactionAsync(
            async tokenCancellation =>
            {
                _ = await _store.GetGroupSettingsAsync(tokenCancellation)
                    ?? throw new DomainException(CommitmentUnavailableMessage);
                var slotId = await legacyStore.GetActionTokenSlotIdAsync(hash, tokenCancellation);
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
                var legacySlot = await RequireSlotAsync(assignment.ShiftSlotId, tokenCancellation);
                var legacyShift = await RequireShiftAsync(legacySlot.ShiftId, tokenCancellation);
                if (!LegacyActionDeadlineOpen(actionToken.Action, legacyShift, now))
                {
                    throw new DomainException("This action no longer applies to the current assignment state.");
                }
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
                QueueNotification(
                    assignment.Id,
                    volunteer.Id,
                    assignment.ShiftSlotId,
                    $"legacy-action:{assignment.Id:N}:{actionToken.Action}:{now:O}",
                    $"Volunteer{actionToken.Action}",
                    now);
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

        var shifts = await _store.GetPublishedCurrentOrFutureShiftsAsync(_clock.UtcNow, cancellationToken);
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
                var access = assignment is null || volunteer is null
                    ? null
                    : await GetAccessStateAsync(
                        volunteer.Id,
                        slot.Id,
                        shift,
                        cancellationToken);
                result.Add(new CoverageDto(
                    slot.Id,
                    shift.Id,
                    assignment?.Id,
                    BuildCommitment(shift, settings, slot, SlotLabel(slot)),
                    state,
                    volunteer?.AnonymizedAtUtc.HasValue == true ? "Removed volunteer" : volunteer?.Name,
                    volunteer?.AnonymizedAtUtc.HasValue == true ? null : volunteer?.Email,
                    access));
            }
        }

        return result.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.State == "Uncovered" ? 0 : x.State == "Unconfirmed" ? 1 : 2).ThenBy(x => x.SlotLabel).ToArray();
    }

    public async Task<IReadOnlyList<AuditDto>> ListAuditAsync(int limit, CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        var timeZoneId = settings?.TimeZoneId ?? "Etc/UTC";
        var entries = await _store.GetAuditEntriesAsync(Math.Clamp(limit, 1, 500), cancellationToken);
        return entries
            .Select(x => new AuditDto(
                x.OccurredAtUtc,
                x.Actor,
                x.Action,
                x.EntityKind,
                x.EntityId,
                x.DetailJson)
            {
                Summary = HumanAuditSummary(x.Action),
                GroupTimeZoneId = timeZoneId
            })
            .ToArray();
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
                0);
        }

        if (state.Volunteer.AnonymizedAtUtc.HasValue)
        {
            return new VolunteerAnonymizationResult(
                VolunteerAnonymizationOutcome.AlreadyAnonymized,
                VolunteerAnonymizationBlocker.None,
                0,
                state.Volunteer.AnonymizedAtUtc,
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

        var invalidatedActionTokens = 0;
        foreach (var actionToken in state.UnusedActionTokens)
        {
            actionToken.Invalidate(now);
            invalidatedActionTokens++;
        }
        var invalidatedCapabilities = 0;
        var invalidatedRecoveryTokens = 0;
        if (_store is IAccessStore accessStore)
        {
            foreach (var capability in await accessStore.GetCapabilitiesForVolunteerAsync(
                         state.Volunteer.Id,
                         cancellationToken))
            {
                if (capability.Invalidate(now))
                {
                    invalidatedCapabilities++;
                }
            }

            foreach (var recovery in await accessStore.GetRecoveryTokensForVolunteerAsync(
                         state.Volunteer.Id,
                         cancellationToken))
            {
                if (recovery.Invalidate(now))
                {
                    invalidatedRecoveryTokens++;
                }
            }
        }

        if (_store is INotificationOutboxStore outbox)
        {
            foreach (var intent in await outbox.GetNotificationIntentsForVolunteerAsync(
                         state.Volunteer.Id,
                         cancellationToken))
            {
                intent.Cancel(now, "ContactRemoved");
            }
        }

        const int redactedNotifications = 0;

        if (!state.Volunteer.Anonymize(now))
        {
            return new VolunteerAnonymizationResult(
                VolunteerAnonymizationOutcome.AlreadyAnonymized,
                VolunteerAnonymizationBlocker.None,
                0,
                anchor,
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
                ActionTokensInvalidated = invalidatedActionTokens,
                CapabilitiesInvalidated = invalidatedCapabilities,
                RecoveryTokensInvalidated = invalidatedRecoveryTokens,
                NotificationDestinationsRedacted = redactedNotifications
            })));

        return new VolunteerAnonymizationResult(
            VolunteerAnonymizationOutcome.Anonymized,
            VolunteerAnonymizationBlocker.None,
            0,
            anchor,
            redactedNotifications)
        {
            RequestCount = state.Requests.Count,
            ActionTokensInvalidated = invalidatedActionTokens,
            AssignmentCount = state.Assignments.Count,
            CapabilitiesInvalidated = invalidatedCapabilities,
            RecoveryTokensInvalidated = invalidatedRecoveryTokens
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
            0);


    private async Task CancelPendingAccessIntentsAsync(
        Guid volunteerId,
        Guid slotId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_store is not INotificationOutboxStore outbox)
        {
            return;
        }

        var intents = await outbox.GetNotificationIntentsForVolunteerAsync(
            volunteerId,
            cancellationToken);
        foreach (var intent in intents.Where(intent =>
                     intent.ShiftSlotId == slotId &&
                     IsAccessIntentKind(intent.Kind) &&
                     intent.State is NotificationIntentState.Pending or
                         NotificationIntentState.RetryScheduled or
                         NotificationIntentState.InFlight))
        {
            intent.Cancel(now, "CommitmentNoLongerEligible");
        }
    }

    private static bool IsAccessIntentKind(string kind) =>
        string.Equals(kind, "RequestReceipt", StringComparison.Ordinal) ||
        kind.Contains("Access", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("Reissue", StringComparison.OrdinalIgnoreCase);
    private static bool IsLinkBearingNotificationKind(string kind) =>
        string.Equals(kind, "RequestReceipt", StringComparison.Ordinal) ||
        string.Equals(kind, "AssignmentAccess", StringComparison.Ordinal) ||
        string.Equals(kind, "AccessRecovery", StringComparison.Ordinal) ||
        string.Equals(kind, "CoordinatorAccessReissue", StringComparison.Ordinal);

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

            if (_store is IAccessStore accessStore)
            {
                foreach (var capability in await accessStore.GetActiveCapabilitiesAsync(
                             assignment.VolunteerId,
                             assignment.ShiftSlotId,
                             cancellationToken))
                {
                    capability.Invalidate(now);
                }

                foreach (var recovery in await accessStore.GetRecoveryTokensForVolunteerAsync(
                             assignment.VolunteerId,
                             cancellationToken))
                {
                    if (recovery.ShiftSlotId == assignment.ShiftSlotId)
                    {
                        recovery.Invalidate(now);
                    }
                }
            }
            await CancelPendingAccessIntentsAsync(
                assignment.VolunteerId,
                assignment.ShiftSlotId,
                now,
                cancellationToken);

            QueueNotification(
                assignment.Id,
                assignment.VolunteerId,
                assignment.ShiftSlotId,
                $"assignment:{assignment.Id:N}:cancelled:{auditAction}",
                "Cancellation",
                now);
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

            if (_store is IAccessStore accessStore)
            {
                foreach (var capability in await accessStore.GetActiveCapabilitiesAsync(
                             assignment.VolunteerId,
                             assignment.ShiftSlotId,
                             cancellationToken))
                {
                    capability.Invalidate(now);
                }

                foreach (var recovery in await accessStore.GetRecoveryTokensForVolunteerAsync(
                             assignment.VolunteerId,
                             cancellationToken))
                {
                    if (recovery.ShiftSlotId == assignment.ShiftSlotId)
                    {
                        recovery.Invalidate(now);
                    }
                }
            }

            await CancelPendingAccessIntentsAsync(
                assignment.VolunteerId,
                assignment.ShiftSlotId,
                now,
                cancellationToken);

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

    private async Task SupersedeOtherRequestsAsync(
        Guid slotId,
        Guid? approvedRequestId,
        string coordinatorEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requests = await _store.GetPendingRequestsForSlotAsync(slotId, cancellationToken);
        foreach (var request in requests.Where(x => x.Id != approvedRequestId))
        {
            request.Supersede(coordinatorEmail, now);
            await CancelPendingAccessIntentsAsync(
                request.VolunteerId,
                request.ShiftSlotId,
                now,
                cancellationToken);
            QueueNotification(
                request.Id,
                request.VolunteerId,
                request.ShiftSlotId,
                $"request:{request.Id:N}:superseded",
                "RequestDecision",
                now);
        }
    }

    private async Task<(ActionToken Token, Assignment Assignment)> ResolveActionAsync(string rawToken, CancellationToken cancellationToken) =>
        await ResolveActionAsync(HashRequiredToken(rawToken), cancellationToken);

    private async Task<(ActionToken Token, Assignment Assignment)> ResolveActionAsync(byte[] hash, CancellationToken cancellationToken)
    {
        if (_store is not ILegacyActionTokenStore legacyStore)
        {
            throw new DomainException("This action link is invalid, expired, or already used.");
        }

        var actionToken = await legacyStore.GetActionTokenByHashAsync(hash, cancellationToken);
        if (actionToken is null || !_tokens.FixedTimeEquals(hash, actionToken.TokenHash) || !actionToken.IsUsable(_clock.UtcNow))
        {
            throw new DomainException("This action link is invalid, expired, or already used.");
        }

        var assignment = await RequireAssignmentAsync(actionToken.AssignmentId, cancellationToken);
        if (!assignment.IsActive ||
            (await RequireVolunteerAsync(assignment.VolunteerId, cancellationToken)).AnonymizedAtUtc.HasValue)
        {
            throw new DomainException("This action link is invalid, expired, or already used.");
        }

        return (actionToken, assignment);
    }


    private async Task<AccessStateDto?> GetAccessStateAsync(
        Guid volunteerId,
        Guid slotId,
        Shift shift,
        CancellationToken cancellationToken)
    {
        if (_store is not IAccessStore accessStore)
        {
            return null;
        }

        var now = _clock.UtcNow;
        var capabilities = await accessStore.GetActiveCapabilitiesAsync(
            volunteerId,
            slotId,
            cancellationToken);
        var intents = _store is INotificationOutboxStore outbox
            ? await outbox.GetNotificationIntentsForVolunteerAsync(volunteerId, cancellationToken)
            : [];
        var latest = intents
            .Where(x => x.ShiftSlotId == slotId && IsAccessIntentKind(x.Kind))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (volunteerId == Guid.Empty)
        {
            return new AccessStateDto("Revoked");
        }

        if (latest?.State is NotificationIntentState.Failed or
            NotificationIntentState.Bounced or
            NotificationIntentState.Complained)
        {
            return new AccessStateDto("Message not sent", latest.CreatedAtUtc);
        }

        if (latest?.State is NotificationIntentState.Pending or
            NotificationIntentState.RetryScheduled or
            NotificationIntentState.InFlight)
        {
            return new AccessStateDto("Delivery pending", latest.CreatedAtUtc);
        }

        if (capabilities.Any() && now <= shift.EndsAtUtc.AddDays(7))
        {
            return new AccessStateDto("Active", latest?.CreatedAtUtc);
        }

        return new AccessStateDto(
            now > shift.EndsAtUtc.AddDays(7) ? "Expired" : "Revoked",
            latest?.CreatedAtUtc);
    }

    private async Task<IReadOnlyList<(Guid VolunteerId, Guid SlotId)>> FindRecoveryMatchesAsync(
        string normalizedEmail,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        var volunteerId = await _store.GetVolunteerIdByNormalizedEmailAsync(
            normalizedEmail,
            cancellationToken);
        if (!volunteerId.HasValue)
        {
            return [];
        }

        var volunteer = await _store.GetVolunteerAsync(volunteerId.Value, cancellationToken);
        if (volunteer is null || volunteer.AnonymizedAtUtc.HasValue)
        {
            return [];
        }

        var now = _clock.UtcNow;
        var requests = await _store.GetRequestsForVolunteerAsync(volunteer.Id, cancellationToken);
        var assignments = await _store.GetAssignmentsForVolunteerAsync(volunteer.Id, cancellationToken);
        var slotIds = requests.Select(x => x.ShiftSlotId)
            .Concat(assignments.Select(x => x.ShiftSlotId))
            .Distinct()
            .ToArray();
        var matches = new List<(Guid VolunteerId, Guid SlotId)>();
        foreach (var slotId in slotIds)
        {
            var latestRequest = requests
                .Where(x => x.ShiftSlotId == slotId)
                .OrderByDescending(x => x.RequestedAtUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
            var activeAssignment = assignments
                .Where(x => x.ShiftSlotId == slotId && x.IsActive)
                .OrderByDescending(x => x.AssignedAtUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
            if (activeAssignment is null && latestRequest?.Status != RequestStatus.Pending)
            {
                continue;
            }

            var slot = await _store.GetSlotAsync(slotId, cancellationToken);
            if (slot is null || !slot.IsActive)
            {
                continue;
            }

            var shift = await _store.GetShiftAsync(slot.ShiftId, cancellationToken);
            if (shift is null ||
                !shift.IsActive ||
                shift.StartsAtUtc < startUtc ||
                shift.StartsAtUtc >= endUtc ||
                now > shift.EndsAtUtc.AddDays(7))
            {
                continue;
            }

            matches.Add((volunteer.Id, slot.Id));
        }

        return matches
            .Distinct()
            .OrderBy(x => x.SlotId)
            .Take(3)
            .ToArray();
    }

    private async Task<Assignment?> FindLatestAssignmentAsync(
        Guid volunteerId,
        Guid slotId,
        CancellationToken cancellationToken)
    {
        var assignments = await _store.GetAssignmentsForVolunteerAsync(volunteerId, cancellationToken);
        return assignments
            .Where(x => x.ShiftSlotId == slotId)
            .OrderByDescending(x => x.AssignedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
    }


    private NotificationIntent? QueueNotification(
        Guid transitionId,
        Guid volunteerId,
        Guid? slotId,
        string eventKey,
        string kind,
        DateTimeOffset nowUtc)
    {
        if (_store is not INotificationOutboxStore outbox)
        {
            return null;
        }

        var intent = NotificationIntent.Create(
            eventKey,
            transitionId,
            volunteerId,
            slotId,
            kind,
            nowUtc);
        outbox.AddNotificationIntent(intent);
        return intent;
    }

    private static IReadOnlyList<string> OfferedHubActions(
        Assignment? assignment,
        Shift shift,
        DateTimeOffset nowUtc)
    {
        if (assignment is null)
        {
            return [];
        }

        if (assignment.Status == AssignmentStatus.Assigned && nowUtc < shift.StartsAtUtc)
        {
            return ["Confirm", "Decline"];
        }

        if (assignment.Status == AssignmentStatus.Confirmed && nowUtc < shift.EndsAtUtc)
        {
            return ["Cancel"];
        }

        return [];
    }

    private static string HubStatusMessage(
        string requestStatus,
        string? assignmentStatus,
        IReadOnlyList<string> actions,
        Shift shift,
        DateTimeOffset nowUtc)
    {
        if (actions.Count > 0)
        {
            return $"Your commitment is {assignmentStatus ?? requestStatus}. Choose an available action below.";
        }

        if (assignmentStatus is null)
        {
            return requestStatus == nameof(RequestStatus.Pending)
                ? "Your request is waiting for coordinator review."
                : $"Your request is {requestStatus}.";
        }

        if (assignmentStatus == nameof(AssignmentStatus.Assigned) && nowUtc >= shift.StartsAtUtc)
        {
            return "The confirmation deadline has passed. This page is read-only.";
        }

        if (assignmentStatus == nameof(AssignmentStatus.Confirmed) && nowUtc >= shift.EndsAtUtc)
        {
            return "The cancellation deadline has passed. This page is read-only.";
        }

        return $"Your assignment is {assignmentStatus}. This page is read-only.";
    }

    private static DomainException InvalidHub() =>
        new("This commitment link is invalid or has expired.");

    private static DomainException InvalidRecovery() =>
        new("This recovery link is invalid or has expired.");

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
    private async Task EnsureExpectedSettingsVersionAsync(
        uint? expectedSettingsVersion,
        CancellationToken cancellationToken)
    {
        if (!expectedSettingsVersion.HasValue)
        {
            return;
        }

        await _store.LockGroupSettingsAsync(cancellationToken);
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        if (settings is null || settings.Version != expectedSettingsVersion.Value)
        {
            throw new DomainException(StalePreviewMessage);
        }
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
                messages.Add("Choose one labelled interpretation for each repeated local time.");
            }

            throw new DomainException(
                messages.Count == 0
                    ? "Choose valid local start and end times."
                    : string.Join(" ", messages.Distinct(StringComparer.Ordinal)));
        }

        return (resolution.StartsAtUtc.GetValueOrDefault(), resolution.EndsAtUtc.GetValueOrDefault());
    }


    private static IReadOnlyList<SetupStepDto> BuildSetupSteps(
        CoordinatorHomeProjection projection,
        bool settingsConfigured)
    {
        var firstShift = projection.FirstUnpublishedShift;
        var expiredShift = projection.FirstExpiredUnpublishedShift;
        var hasFirstShift = firstShift is not null || expiredShift is not null;
        var publishUrl = firstShift is null
            ? null
            : $"/Coordinator/Schedule/Publish/{firstShift.Id}";
        var reviewUrl = publishUrl ?? (expiredShift is null
            ? null
            : $"/Coordinator/Schedule/Edit/{expiredShift.Id}");
        var reviewDescription = firstShift is null && expiredShift is not null
            ? "Correct the expired schedule entry before reviewing what volunteers will see."
            : "Check the commitment details and the openings before they become public.";

        return
        [
            new SetupStepDto(
                1,
                "Set your group time zone",
                "Choose the local time zone used for every schedule entry and commitment.",
                settingsConfigured ? "Complete" : "Next",
                settingsConfigured,
                !settingsConfigured,
                false,
                "/Coordinator/Settings"),
            new SetupStepDto(
                2,
                "Understand volunteer requests",
                "Volunteers request a commitment. A coordinator reviews each request before anyone is assigned.",
                "Policy in use",
                true,
                false,
                true,
                null),
            new SetupStepDto(
                3,
                "Create the first schedule entry",
                "Enter the first shift, its local times, location, instructions, and slots.",
                hasFirstShift ? "Complete" : settingsConfigured ? "Next" : "Not started",
                hasFirstShift,
                settingsConfigured && !hasFirstShift,
                false,
                settingsConfigured ? "/Coordinator/Schedule/Create" : null),
            new SetupStepDto(
                4,
                "Review what volunteers will see",
                reviewDescription,
                projection.HasPublishedShift ? "Complete" : hasFirstShift ? "Next" : "Not started",
                projection.HasPublishedShift,
                settingsConfigured && hasFirstShift && !projection.HasPublishedShift,
                false,
                reviewUrl),
            new SetupStepDto(
                5,
                "Publish open commitments",
                "Publish the reviewed openings so volunteers can request them.",
                projection.HasPublishedShift ? "Complete" : "Not started",
                projection.HasPublishedShift,
                false,
                false,
                publishUrl)
        ];
    }

    private static void AddAttention(
        ICollection<CoordinatorAttentionDto> attention,
        string key,
        string label,
        int count,
        string url,
        string actionLabel,
        IReadOnlyList<CoordinatorHomeExample> examples,
        GroupSettings settings)
    {
        if (count == 0)
        {
            return;
        }

        attention.Add(new CoordinatorAttentionDto(
            key,
            label,
            count,
            url,
            actionLabel,
            examples
                .Select(example => new CoordinatorAttentionExampleDto(
                    BuildCommitment(example, settings),
                    example.VolunteerName,
                    example.OccurredAtUtc,
                    MessagePurpose(example.MessageKind)))
                .ToArray()));
    }

    private static CommitmentDto BuildCommitment(
        CoordinatorHomeExample example,
        GroupSettings settings) =>
        new(
            example.ShiftId,
            example.SlotId,
            example.ShiftTitle,
            example.StartsAtUtc,
            example.EndsAtUtc,
            settings.TimeZoneId,
            null,
            example.SlotLabel,
            null);

    private static string? MessagePurpose(string? kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? null
            : kind.Contains("Request", StringComparison.OrdinalIgnoreCase)
                ? "Request update"
                : kind.Contains("Assignment", StringComparison.OrdinalIgnoreCase)
                    ? "Assignment update"
                    : "Commitment update";

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
    private static string HumanAuditSummary(string action) => action switch
    {
        "GroupTimeZoneConfigured" => "The group time zone was configured.",
        "GroupTimeZoneChanged" => "The group time zone was changed.",
        "ShiftCreated" => "A schedule entry was created.",
        "ShiftEdited" => "A schedule entry was corrected.",
        "ShiftPublished" => "A schedule entry was published.",
        "ShiftDeactivated" => "A schedule entry was deactivated and its workflow was resolved.",
        "RequestSubmitted" => "A volunteer request was received.",
        "RequestApproved" => "A volunteer request was approved and assigned.",
        "RequestRejected" => "A volunteer request was declined.",
        "AssignmentCreatedOrReassigned" => "A volunteer assignment was created or replaced.",
        "AssignmentReassigned" => "An earlier volunteer assignment was replaced.",
        "AssignmentCancelledByCoordinator" => "A coordinator cancelled a volunteer assignment.",
        "AssignmentCancelledByShiftDeactivation" => "A volunteer assignment was cancelled because its schedule entry was deactivated.",
        "VolunteerAnonymized" => "A volunteer's identifying contact data was removed.",
        _ when action.StartsWith("Assignment", StringComparison.Ordinal) => "A volunteer response changed an assignment.",
        _ => "A recorded coordinator action occurred."
    };


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

    private static string? DisplayVolunteerName(Volunteer? volunteer) =>
        volunteer is null
            ? null
            : volunteer.AnonymizedAtUtc.HasValue
                ? "Removed volunteer"
                : volunteer.Name;

    private static string AssignmentStateLabel(Assignment? assignment) => assignment?.Status switch
    {
        AssignmentStatus.Assigned => "Waiting for confirmation",
        AssignmentStatus.Confirmed => "Confirmed",
        _ => "Open"
    };

    private static bool MatchesExpectedAssignment(
        Assignment? actual,
        Guid? expectedAssignmentId,
        Guid? expectedVolunteerId,
        string? expectedAssignmentState)
    {
        if (!expectedAssignmentId.HasValue)
        {
            return actual is null;
        }

        return actual is not null &&
               actual.Id == expectedAssignmentId.Value &&
               (!expectedVolunteerId.HasValue || actual.VolunteerId == expectedVolunteerId.Value) &&
               (string.IsNullOrWhiteSpace(expectedAssignmentState) ||
                string.Equals(actual.Status.ToString(), expectedAssignmentState, StringComparison.Ordinal));
    }

    private static bool CanApply(VolunteerAction action, AssignmentStatus status) => action switch
    {
        VolunteerAction.Confirm => status == AssignmentStatus.Assigned,
        VolunteerAction.Decline => status == AssignmentStatus.Assigned,
        VolunteerAction.Cancel => status is AssignmentStatus.Assigned or AssignmentStatus.Confirmed,
        _ => false
    };
    private static bool LegacyActionDeadlineOpen(
        VolunteerAction action,
        Shift shift,
        DateTimeOffset nowUtc) =>
        action switch
        {
            VolunteerAction.Confirm or VolunteerAction.Decline => nowUtc < shift.StartsAtUtc,
            VolunteerAction.Cancel => nowUtc < shift.EndsAtUtc,
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
    private static string BuildAffectedSet(
        IEnumerable<Guid> requestIds,
        IEnumerable<Guid> assignmentIds) =>
        $"requests:{string.Join(",", requestIds.OrderBy(x => x).Select(x => x.ToString("N")))};" +
        $"assignments:{string.Join(",", assignmentIds.OrderBy(x => x).Select(x => x.ToString("N")))}";
    private static string BuildAssignmentAffectedSet(
        IEnumerable<Guid> requestIds,
        IEnumerable<Guid> assignmentIds,
        Guid? selectedVolunteerId) =>
        BuildAffectedSet(requestIds, assignmentIds) +
        $";volunteers:{(selectedVolunteerId.HasValue ? selectedVolunteerId.Value.ToString("N") : string.Empty)}";


    private static int SlotOrder(ShiftSlot slot) => slot.Kind == SlotKind.Primary ? 0 : slot.Position;

    private static string Detail<T>(T value) => JsonSerializer.Serialize(value);
}
