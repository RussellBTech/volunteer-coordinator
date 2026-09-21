using System.Text.Json;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Application.Time;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Commitments;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Volunteers;

namespace VolunteerCoordinator.Application;

public sealed class RecurringCommitmentService
{
    public const int DefaultHorizonWeeks = 12;
    public const int MinimumHorizonWeeks = 4;
    public const int MaximumHorizonWeeks = 26;
    public const string JustClaimedMessage = "This commitment was just claimed. Choose another opening.";

    private readonly IWorkflowStore _workflowStore;
    private readonly IRecurringShiftStore _recurringStore;
    private readonly IRecurringCommitmentStore _commitmentStore;
    private readonly IClock _clock;
    private readonly ITokenService _tokens;
    private readonly ITransientLinkMaterialStore? _transientLinkMaterial;

    private sealed record Candidate(
        RecurringShiftOccurrence Occurrence,
        Shift? Shift,
        ShiftSlot? Slot,
        DateTimeOffset StartsAtUtc,
        bool Included,
        string State,
        string? Reason);
    private sealed record HandoffTarget(
        RecurringShiftOccurrence Occurrence,
        Shift Shift,
        ShiftSlot Slot);

    private sealed record MaterializedAccess(NotificationIntent? Intent, string RawToken);
    private sealed record SubmissionWork(
        RecurringCommitmentSubmission Submission,
        NotificationIntent? Intent);

    public RecurringCommitmentService(
        IWorkflowStore workflowStore,
        IRecurringShiftStore recurringStore,
        IRecurringCommitmentStore commitmentStore,
        IClock clock,
        ITokenService tokens,
        ITransientLinkMaterialStore? transientLinkMaterial = null)
    {
        _workflowStore = workflowStore;
        _recurringStore = recurringStore;
        _commitmentStore = commitmentStore;
        _clock = clock;
        _tokens = tokens;
        _transientLinkMaterial = transientLinkMaterial;
    }

    public async Task<RecurringCommitmentPreviewDto> PreviewAsync(
        Guid seriesId,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        int horizonWeeks,
        CancellationToken cancellationToken)
    {
        ValidateHorizon(horizonWeeks);
        var series = await RequireSeriesAsync(seriesId, cancellationToken);
        var revision = await RequireCurrentRevisionAsync(series, cancellationToken);
        ValidateRole(roleKind, rolePosition);
        var endLocalDate = EndDate(effectiveLocalDate, horizonWeeks);
        var candidates = await BuildCandidatesAsync(
            seriesId,
            revision,
            roleKind,
            rolePosition,
            effectiveLocalDate,
            endLocalDate,
            cancellationToken);
        if (!candidates.Any(x => x.Included))
        {
            throw new DomainException("No eligible published occurrence is available in that date range.");
        }

        var dates = candidates.Select(ToDateDto).ToArray();
        return new RecurringCommitmentPreviewDto(
            seriesId,
            revision.Id,
            revision.Title,
            revision.SignupPolicy,
            PolicyConsequence(revision.SignupPolicy),
            roleKind,
            rolePosition,
            RoleLabel(roleKind, rolePosition),
            effectiveLocalDate,
            endLocalDate,
            horizonWeeks,
            dates,
            candidates.Count(x => x.Included),
            candidates.Count(x => !x.Included),
            series.Version);
    }

    public async Task<RecurringCommitmentSubmission> SubmitAsync(
        Guid seriesId,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        int horizonWeeks,
        uint expectedSeriesVersion,
        string name,
        string email,
        string? phone,
        CancellationToken cancellationToken)
    {
        ValidateHorizon(horizonWeeks);
        var now = _clock.UtcNow;
        var generatedToken = _tokens.Generate();
        var accessIntents = new List<MaterializedAccess>();
        var result = await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _recurringStore.LockRecurringSeriesAsync(seriesId, token);
                await _commitmentStore.LockRecurringCommitmentRoleAsync(seriesId, roleKind, rolePosition, token);
                var series = await RequireSeriesAsync(seriesId, token);
                if (series.Version != expectedSeriesVersion)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                var revision = await RequireCurrentRevisionAsync(series, token);
                var endLocalDate = EndDate(effectiveLocalDate, horizonWeeks);
                var candidates = await BuildCandidatesAsync(
                    seriesId,
                    revision,
                    roleKind,
                    rolePosition,
                    effectiveLocalDate,
                    endLocalDate,
                    token);
                var included = candidates.Where(x => x.Included).OrderBy(x => x.Slot!.Id).ToArray();
                if (included.Length == 0)
                {
                    throw new DomainException("No eligible published occurrence is available in that date range.");
                }

                if (await _commitmentStore.GetOverlappingRecurringCommitmentsAsync(
                        seriesId,
                        roleKind,
                        rolePosition,
                        effectiveLocalDate,
                        endLocalDate,
                        token) is { Count: > 0 })
                {
                    throw new DomainException(JustClaimedMessage);
                }

                var normalizedEmail = Volunteer.NormalizeEmail(email);
                var existingVolunteerId = await _workflowStore.GetVolunteerIdByNormalizedEmailAsync(normalizedEmail, token);
                Volunteer volunteer;
                if (existingVolunteerId.HasValue)
                {
                    await _workflowStore.LockVolunteerAsync(existingVolunteerId.Value, token);
                    volunteer = await RequireVolunteerAsync(existingVolunteerId.Value, token);
                    if (volunteer.AnonymizedAtUtc.HasValue)
                    {
                        throw new DomainException("Removed volunteer contact data cannot be restored.");
                    }
                }
                else
                {
                    volunteer = Volunteer.Create(name, email, phone, now);
                    _workflowStore.AddVolunteer(volunteer);
                }

                if (revision.SignupPolicy == SignupPolicy.ApprovalRequired)
                {
                    var pending = (await _commitmentStore.GetPendingRecurringCommitmentRequestsAsync(seriesId, token))
                        .SingleOrDefault(x =>
                            x.VolunteerId == volunteer.Id &&
                            x.RoleKind == roleKind &&
                            x.RolePosition == rolePosition &&
                            x.EffectiveLocalDate == effectiveLocalDate &&
                            x.EndLocalDate == endLocalDate);
                    if (pending is not null)
                    {
                        throw new DomainException("You already have a pending recurring request for that date range.");
                    }

                    var request = RecurringCommitmentRequest.Create(
                        seriesId,
                        revision.Id,
                        volunteer.Id,
                        roleKind,
                        rolePosition,
                        effectiveLocalDate,
                        endLocalDate,
                        revision.SignupPolicy,
                        now);
                    _commitmentStore.AddRecurringCommitmentRequest(request);
                    var capability = RecurringCommitmentCapability.Create(
                        volunteer.Id,
                        request.Id,
                        null,
                        generatedToken.Hash,
                        now);
                    _commitmentStore.AddRecurringCommitmentCapability(capability);
                    var requestIntent = QueueNotification(
                        request.Id,
                        volunteer.Id,
                        included[0].Slot!.Id,
                        $"recurring-request:{request.Id:N}:receipt",
                        "RecurringCommitmentRequest",
                        now);
                    _workflowStore.AddAuditEntry(AuditEntry.Create(
                        now,
                        $"volunteer:{volunteer.Id}",
                        "RecurringCommitmentRequested",
                        nameof(RecurringCommitmentRequest),
                        request.Id,
                        Detail(new
                        {
                            request.SeriesId,
                            request.RoleKind,
                            request.RolePosition,
                            request.EffectiveLocalDate,
                            request.EndLocalDate,
                            request.SourcePolicy,
                            Included = included.Length,
                            Skipped = candidates.Count - included.Length
                        }), shiftId: included[0].Shift!.Id, volunteerId: volunteer.Id));
                    return new SubmissionWork(
                        new RecurringCommitmentSubmission(
                            request.Id,
                            Guid.Empty,
                            generatedToken.RawToken,
                            null),
                        requestIntent);
                }

                var commitment = RecurringCommitment.Create(
                    seriesId,
                    revision.Id,
                    volunteer.Id,
                    roleKind,
                    rolePosition,
                    effectiveLocalDate,
                    endLocalDate,
                    revision.SignupPolicy,
                    null,
                    now);
                _commitmentStore.AddRecurringCommitment(commitment);
                var recurringCapability = RecurringCommitmentCapability.Create(
                    volunteer.Id,
                    null,
                    commitment.Id,
                    generatedToken.Hash,
                    now);
                _commitmentStore.AddRecurringCommitmentCapability(recurringCapability);

                var joins = await MaterializeAssignmentsAsync(
                    commitment,
                    candidates,
                    volunteer,
                    now,
                    confirmed: true,
                    actor: "DIRECT CLAIM",
                    accessIntents,
                    token);
                if (joins.Count(x => x.State == RecurringCommitmentOccurrenceState.Confirmed) == 0)
                {
                    throw new DomainException("No eligible published occurrence is available in that date range.");
                }

                var intent = QueueNotification(
                    commitment.Id,
                    volunteer.Id,
                    included[0].Slot!.Id,
                    $"recurring:{commitment.Id:N}:access",
                    "RecurringCommitmentAccess",
                    now);
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    $"volunteer:{volunteer.Id}",
                    "RecurringCommitmentDirectClaimed",
                    nameof(RecurringCommitment),
                    commitment.Id,
                    Detail(new
                    {
                        commitment.SeriesId,
                        commitment.RoleKind,
                        commitment.RolePosition,
                        commitment.EffectiveLocalDate,
                        commitment.EndLocalDate,
                        Included = joins.Count(x => x.State == RecurringCommitmentOccurrenceState.Confirmed),
                        Skipped = joins.Count(x => x.State == RecurringCommitmentOccurrenceState.SkippedException)
                    }), shiftId: included[0].Shift!.Id, volunteerId: volunteer.Id));
                return new SubmissionWork(
                    new RecurringCommitmentSubmission(
                        Guid.Empty,
                        commitment.Id,
                        generatedToken.RawToken,
                        null),
                    intent);
            },
            cancellationToken);

        PutHubToken(result.Intent, generatedToken.RawToken, now);
        foreach (var material in accessIntents)
        {
            PutHubToken(material.Intent, material.RawToken, now);
        }

        return result.Submission;
    }

    public async Task<Guid> ApproveAsync(
        Guid requestId,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var accessIntents = new List<MaterializedAccess>();
        var commitmentId = await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                var request = await RequireRequestAsync(requestId, token);
                if (!request.IsPending)
                {
                    throw new DomainException("Only a pending recurring request can be approved.");
                }

                await _recurringStore.LockRecurringSeriesAsync(request.SeriesId, token);
                await _commitmentStore.LockRecurringCommitmentRoleAsync(
                    request.SeriesId,
                    request.RoleKind,
                    request.RolePosition,
                    token);
                request = await RequireRequestAsync(requestId, token);
                if (!request.IsPending)
                {
                    throw new DomainException("Only a pending recurring request can be approved.");
                }
                var conflictingRequests = (await _commitmentStore.GetPendingRecurringCommitmentRequestsAsync(
                        request.SeriesId,
                        token))
                    .Where(x =>
                        x.Id != request.Id &&
                        x.RoleKind == request.RoleKind &&
                        x.RolePosition == request.RolePosition &&
                        x.EffectiveLocalDate <= request.EndLocalDate &&
                        x.EndLocalDate >= request.EffectiveLocalDate)
                    .OrderBy(x => x.RequestedAtUtc)
                    .ThenBy(x => x.Id)
                    .ToArray();


                var series = await RequireSeriesAsync(request.SeriesId, token);
                var revision = await RequireRevisionAsync(request.RevisionId, token);
                var volunteer = await RequireVolunteerAsync(request.VolunteerId, token);
                if (volunteer.AnonymizedAtUtc.HasValue)
                {
                    throw new DomainException("Removed volunteer contact data cannot be restored.");
                }
                var candidates = await BuildCandidatesAsync(
                    request.SeriesId,
                    revision,
                    request.RoleKind,
                    request.RolePosition,
                    request.EffectiveLocalDate,
                    request.EndLocalDate,
                    token);
                var included = candidates.Where(x => x.Included).OrderBy(x => x.Slot!.Id).ToArray();
                if (included.Length == 0)
                {
                    throw new DomainException("No eligible published occurrence remains for this recurring request.");
                }

                if (await _commitmentStore.GetOverlappingRecurringCommitmentsAsync(
                        request.SeriesId,
                        request.RoleKind,
                        request.RolePosition,
                        request.EffectiveLocalDate,
                        request.EndLocalDate,
                        token) is { Count: > 0 })
                {
                    throw new DomainException(JustClaimedMessage);
                }

                var commitment = RecurringCommitment.Create(
                    request.SeriesId,
                    request.RevisionId,
                    request.VolunteerId,
                    request.RoleKind,
                    request.RolePosition,
                    request.EffectiveLocalDate,
                    request.EndLocalDate,
                    request.SourcePolicy,
                    request.Id,
                    now);
                _commitmentStore.AddRecurringCommitment(commitment);
                request.Approve(actor, now, commitment.Id);
                var capability = await _commitmentStore.GetActiveRecurringCapabilitiesAsync(
                    volunteer.Id,
                    null,
                    token);
                var requestCapability = capability.FirstOrDefault(x => x.RequestId == request.Id);
                requestCapability?.AttachCommitment(commitment.Id);
                if (requestCapability is null)
                {
                    throw new DomainException("The recurring request access link is no longer available. Ask the coordinator to reissue it.");
                }

                foreach (var conflictingRequest in conflictingRequests)
                {
                    conflictingRequest.Supersede(actor, now);
                    await ResolveRecurringRequestAccessAsync(
                        conflictingRequest,
                        now,
                        token);
                }

                var joins = await MaterializeAssignmentsAsync(
                    commitment,
                    candidates,
                    volunteer,
                    now,
                    confirmed: false,
                    actor,
                    accessIntents,
                    token);
                if (joins.All(x => x.State == RecurringCommitmentOccurrenceState.SkippedException))
                {
                    throw new DomainException("No eligible published occurrence remains for this recurring request.");
                }

                var intent = QueueNotification(
                    commitment.Id,
                    volunteer.Id,
                    included[0].Slot!.Id,
                    $"recurring:{commitment.Id:N}:confirmation",
                    "RecurringCommitmentConfirmation",
                    now);
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,

                    actor,
                    "RecurringCommitmentApproved",
                    nameof(RecurringCommitment),
                    commitment.Id,
                    Detail(new
                    {
                        request.Id,
                        commitment.SeriesId,
                        Assigned = joins.Count(x => x.State == RecurringCommitmentOccurrenceState.Assigned),
                        Skipped = joins.Count(x => x.State == RecurringCommitmentOccurrenceState.SkippedException)
                    }), shiftId: included[0].Shift!.Id, volunteerId: volunteer.Id));
                return commitment.Id;
            },
            cancellationToken);

        foreach (var material in accessIntents)
        {
            PutHubToken(material.Intent, material.RawToken, now);
        }

        return commitmentId;
    }

    public async Task RejectAsync(
        Guid requestId,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                var request = await RequireRequestAsync(requestId, token);
                if (!request.IsPending)
                {
                    throw new DomainException("Only a pending recurring request can be rejected.");
                }

                await _recurringStore.LockRecurringSeriesAsync(request.SeriesId, token);
                await _commitmentStore.LockRecurringCommitmentRoleAsync(
                    request.SeriesId,
                    request.RoleKind,
                    request.RolePosition,
                    token);
                await _commitmentStore.LockRecurringCommitmentRequestAsync(requestId, token);
                request = await RequireRequestAsync(requestId, token);
                if (!request.IsPending)
                {
                    throw new DomainException("Only a pending recurring request can be rejected.");
                }

                request.Reject(actor, now);
                await ResolveRecurringRequestAccessAsync(request, now, token);
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringCommitmentRequestRejected",
                    nameof(RecurringCommitmentRequest),
                    request.Id,
                    Detail(new
                    {
                        request.SeriesId,
                        request.RoleKind,
                        request.RolePosition,
                        request.EffectiveLocalDate,
                        request.EndLocalDate
                    }), volunteerId: request.VolunteerId));
                return true;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringCommitmentRequestDto>> ListRequestsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _commitmentStore.GetCoordinatorRecurringRequestPageAsync(
            _clock.UtcNow,
            50,
            cancellationToken);
        return rows
            .Select(row => new RecurringCommitmentRequestDto(
                row.RequestId,
                row.VolunteerName,
                row.VolunteerEmail,
                row.Title,
                RoleLabel((SlotKind)row.RoleKind, row.RolePosition),
                (SignupPolicy)row.SourcePolicy,
                ((RecurringCommitmentRequestStatus)row.Status).ToString(),
                row.EffectiveLocalDate,
                row.EndLocalDate,
                row.IncludedCount,
                Math.Max(0, row.TotalCount - row.IncludedCount),
                row.Status == (int)RecurringCommitmentRequestStatus.Pending,
                row.RecurringCommitmentId))
            .ToArray();
    }
    public async Task<RecurringCommitmentHandoffPreviewDto> PreviewHandoffAsync(
        Guid commitmentId,
        CancellationToken cancellationToken)
    {
        var commitment = await RequireCommitmentAsync(commitmentId, cancellationToken);
        var dates = await BuildCommitmentDateDtosAsync(commitment, cancellationToken);
        var effectiveLocalDate = dates
            .Where(x =>
                x.StartsAtUtc > _clock.UtcNow &&
                x.State is nameof(RecurringCommitmentOccurrenceState.Assigned) or
                    nameof(RecurringCommitmentOccurrenceState.Confirmed))
            .OrderBy(x => x.LocalDate)
            .Select(x => (DateOnly?)x.LocalDate)
            .FirstOrDefault()
            ?? commitment.EffectiveLocalDate;
        return await PreviewHandoffAsync(commitmentId, effectiveLocalDate, cancellationToken);
    }


    public async Task<RecurringCommitmentHubDto> InspectHubAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        var capability = await GetCapabilityAsync(rawToken, cancellationToken);
        if (capability is null || !capability.IsActive)
        {
            throw InvalidHub();
        }

        if (capability.CommitmentId is null)
        {
            var request = capability.RequestId is Guid requestId
                ? await _commitmentStore.GetRecurringCommitmentRequestAsync(requestId, cancellationToken)
                : null;
            if (request is null)
            {
                throw InvalidHub();
            }

            var revision = await RequireRevisionAsync(request.RevisionId, cancellationToken);
            var requestVolunteer = await RequireVolunteerAsync(request.VolunteerId, cancellationToken);
            if (requestVolunteer.AnonymizedAtUtc.HasValue)
            {
                throw InvalidHub();
            }
            var dates = await BuildRequestDateDtosAsync(request, revision, cancellationToken);
            return new RecurringCommitmentHubDto(
                capability.Id,
                request.Id,
                Guid.Empty,
                requestVolunteer.Name,
                revision.Title,
                request.Status == RecurringCommitmentRequestStatus.Pending ? "Pending coordinator review" : request.Status.ToString(),
                request.SourcePolicy,
                "A coordinator will review this recurring request. The date list remains visible here.",
                request.EffectiveLocalDate,
                request.EndLocalDate,
                dates,
                false,
                false,
                []);
        }

        var commitment = await RequireCommitmentAsync(capability.CommitmentId.Value, cancellationToken);
        var revisionForCommitment = await RequireRevisionAsync(commitment.RevisionId, cancellationToken);
        var volunteer = await RequireVolunteerAsync(commitment.VolunteerId, cancellationToken);
        if (volunteer.AnonymizedAtUtc.HasValue)
        {
            throw InvalidHub();
        }

        var finalOccurrenceEndsAtUtc = await GetFinalOccurrenceEndsAtUtcAsync(
            commitment,
            cancellationToken);
        if (finalOccurrenceEndsAtUtc is DateTimeOffset finalEnd &&
            !capability.IsReadable(_clock.UtcNow, finalEnd))
        {
            throw InvalidHub();
        }
        var datesForCommitment = await BuildCommitmentDateDtosAsync(commitment, cancellationToken);
        var withdrawalDates = datesForCommitment
            .Where(x => x.StartsAtUtc > _clock.UtcNow && x.State is nameof(RecurringCommitmentOccurrenceState.Assigned) or nameof(RecurringCommitmentOccurrenceState.Confirmed))
            .Select(x => x.LocalDate)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        return new RecurringCommitmentHubDto(
            capability.Id,
            commitment.SourceRequestId,
            commitment.Id,
            volunteer.Name,
            revisionForCommitment.Title,
            commitment.State.ToString(),
            commitment.SourcePolicy,
            commitment.State switch
            {
                RecurringCommitmentState.AwaitingConfirmation => "Confirm this recurring commitment once. You will not need to confirm each week.",
                RecurringCommitmentState.Active => "This recurring commitment is active. You can withdraw from a future occurrence.",
                RecurringCommitmentState.Withdrawn => "This recurring commitment was withdrawn. Earlier history remains visible.",
                _ => "This recurring commitment is complete."
            },
            commitment.EffectiveLocalDate,
            commitment.EndLocalDate,
            datesForCommitment,
            commitment.State == RecurringCommitmentState.AwaitingConfirmation,
            commitment.State == RecurringCommitmentState.Active && withdrawalDates.Length > 0,
            withdrawalDates);
    }

    public async Task<CommandResult<string>> ApplyHubActionAsync(
        string rawToken,
        string action,
        DateOnly? effectiveLocalDate,
        CancellationToken cancellationToken)
    {
        var capability = await GetCapabilityAsync(rawToken, cancellationToken)
            ?? throw InvalidHub();
        if (!capability.CommitmentId.HasValue || !capability.IsActive)
        {
            throw InvalidHub();
        }

        var normalizedAction = action.Trim();
        if (normalizedAction is not ("Confirm" or "Withdraw"))
        {
            throw InvalidHub();
        }

        var now = _clock.UtcNow;
        var result = await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _commitmentStore.LockRecurringCommitmentAsync(capability.CommitmentId.Value, token);
                var currentCapability = await GetCapabilityAsync(rawToken, token)
                    ?? throw InvalidHub();
                var commitment = await RequireCommitmentAsync(capability.CommitmentId.Value, token);
                var volunteer = await RequireVolunteerAsync(commitment.VolunteerId, token);
                if (!currentCapability.IsActive || volunteer.AnonymizedAtUtc.HasValue)
                {
                    throw InvalidHub();
                }
                var finalOccurrenceEndsAtUtc = await GetFinalOccurrenceEndsAtUtcAsync(
                    commitment,
                    token);
                if (finalOccurrenceEndsAtUtc is DateTimeOffset finalEnd &&
                    now > finalEnd.AddDays(7))
                {
                    throw InvalidHub();
                }


                return normalizedAction == "Confirm"
                    ? await ConfirmRecurringCommitmentAsync(commitment, volunteer, now, token)
                    : await WithdrawRecurringCommitmentAsync(
                        commitment,
                        currentCapability,
                        volunteer,
                        effectiveLocalDate,
                        now,
                        token);
            },
            cancellationToken);

        return new CommandResult<string>(result.Action, null);
    }

    private async Task<(string Action, Guid VolunteerId)> ConfirmRecurringCommitmentAsync(
        RecurringCommitment commitment,
        Volunteer volunteer,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (commitment.State == RecurringCommitmentState.Active)
        {
            return ("Already confirmed", volunteer.Id);
        }

        if (commitment.State != RecurringCommitmentState.AwaitingConfirmation)
        {
            throw InvalidHub();
        }

        var joins = (await _commitmentStore.GetRecurringCommitmentOccurrencesAsync(
                commitment.Id,
                cancellationToken))
            .OrderBy(x => x.Id)
            .ToArray();
        foreach (var join in joins)
        {
            await _commitmentStore.LockRecurringCommitmentOccurrenceAsync(join.Id, cancellationToken);
            await _recurringStore.LockRecurringOccurrenceAsync(join.RecurringOccurrenceId, cancellationToken);
        }

        var lockedJoins = new List<RecurringCommitmentOccurrence>(joins.Length);
        foreach (var join in joins)
        {
            lockedJoins.Add(
                await _commitmentStore.GetRecurringCommitmentOccurrenceAsync(
                    commitment.Id,
                    join.RecurringOccurrenceId,
                    cancellationToken) ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage));
        }

        if (lockedJoins.Any(x =>
                x.State is RecurringCommitmentOccurrenceState.Replaced or
                    RecurringCommitmentOccurrenceState.Withdrawn))
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var assignedJoins = lockedJoins
            .Where(x => x.State is RecurringCommitmentOccurrenceState.Assigned or
                RecurringCommitmentOccurrenceState.Confirmed)
            .ToArray();
        if (assignedJoins.Length == 0 || assignedJoins.Any(x => !x.AssignmentId.HasValue))
        {
            throw new DomainException("No eligible recurring assignment remains to confirm.");
        }

        var assignments = new Dictionary<Guid, Assignment>();
        foreach (var assignmentId in assignedJoins
                     .Select(x => x.AssignmentId!.Value)
                     .Distinct()
                     .OrderBy(x => x))
        {
            await _workflowStore.LockAssignmentAsync(assignmentId, cancellationToken);
        }

        foreach (var join in assignedJoins)
        {
            var assignment = await _workflowStore.GetAssignmentAsync(join.AssignmentId!.Value, cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            assignments[assignment.Id] = assignment;
        }

        foreach (var shiftId in assignments.Values.Select(x => x.ShiftId).Distinct().OrderBy(x => x))
        {
            await _workflowStore.LockShiftAsync(shiftId, cancellationToken);
        }

        foreach (var slotId in assignments.Values.Select(x => x.ShiftSlotId).Distinct().OrderBy(x => x))
        {
            await _workflowStore.LockSlotAsync(slotId, cancellationToken);
        }

        var confirmedCount = 0;
        Guid? notificationSlotId = null;
        foreach (var join in assignedJoins.OrderBy(x => x.Id))
        {
            var currentJoin = await _commitmentStore.GetRecurringCommitmentOccurrenceAsync(
                    commitment.Id,
                    join.RecurringOccurrenceId,
                    cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            if (currentJoin.State is RecurringCommitmentOccurrenceState.Replaced or
                    RecurringCommitmentOccurrenceState.Withdrawn ||
                !currentJoin.AssignmentId.HasValue)
            {
                throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            }

            var assignment = await _workflowStore.GetAssignmentAsync(
                    currentJoin.AssignmentId.Value,
                    cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            var occurrence = await _recurringStore.GetRecurringOccurrenceAsync(
                    currentJoin.RecurringOccurrenceId,
                    cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            var shift = await RequireShiftAsync(assignment.ShiftId, cancellationToken);
            var slot = await RequireSlotAsync(assignment.ShiftSlotId, cancellationToken);
            if (occurrence.IsException ||
                !shift.IsActive ||
                shift.StartsAtUtc <= nowUtc ||
                !slot.IsActive ||
                assignment.Status is not (AssignmentStatus.Assigned or AssignmentStatus.Confirmed))
            {
                throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            }

            if (assignment.Status == AssignmentStatus.Assigned)
            {
                assignment.Confirm(nowUtc);
            }

            currentJoin.SetAssignment(
                assignment.Id,
                RecurringCommitmentOccurrenceState.Confirmed);
            confirmedCount++;
            notificationSlotId ??= slot.Id;
        }

        if (confirmedCount == 0)
        {
            throw new DomainException("No eligible recurring assignment remains to confirm.");
        }

        commitment.Confirm(nowUtc);
        QueueNotification(
            commitment.Id,
            volunteer.Id,
            notificationSlotId,
            $"recurring:{commitment.Id:N}:confirmed",
            "RecurringCommitmentConfirmed",
            nowUtc);
        _workflowStore.AddAuditEntry(AuditEntry.Create(
            nowUtc,
            "volunteer-token",
            "RecurringCommitmentConfirmed",
            nameof(RecurringCommitment),
            commitment.Id,
            Detail(new { commitment.Id, Confirmed = confirmedCount }),
            shiftId: assignments.Values.Select(x => (Guid?)x.ShiftId).First(),
            volunteerId: volunteer.Id));
        return ("Confirmed", volunteer.Id);
    }

    private async Task<(string Action, Guid VolunteerId)> WithdrawRecurringCommitmentAsync(
        RecurringCommitment commitment,
        RecurringCommitmentCapability capability,
        Volunteer volunteer,
        DateOnly? effectiveLocalDate,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (commitment.State == RecurringCommitmentState.Withdrawn)
        {
            return ("Already withdrawn", volunteer.Id);
        }

        if (commitment.State != RecurringCommitmentState.Active ||
            !effectiveLocalDate.HasValue)
        {
            throw InvalidHub();
        }

        var requestedDate = effectiveLocalDate.Value;
        if (requestedDate < commitment.EffectiveLocalDate ||
            requestedDate > commitment.EndLocalDate)
        {
            throw new DomainException("Choose a future occurrence inside this recurring commitment.");
        }

        var joins = (await _commitmentStore.GetRecurringCommitmentOccurrencesAsync(
                commitment.Id,
                cancellationToken))
            .OrderBy(x => x.Id)
            .ToArray();
        foreach (var join in joins)
        {
            await _commitmentStore.LockRecurringCommitmentOccurrenceAsync(join.Id, cancellationToken);
            await _recurringStore.LockRecurringOccurrenceAsync(join.RecurringOccurrenceId, cancellationToken);
        }

        var lockedJoins = new List<RecurringCommitmentOccurrence>(joins.Length);
        var occurrences = new Dictionary<Guid, RecurringShiftOccurrence>();
        foreach (var join in joins)
        {
            var currentJoin = await _commitmentStore.GetRecurringCommitmentOccurrenceAsync(
                    commitment.Id,
                    join.RecurringOccurrenceId,
                    cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            lockedJoins.Add(currentJoin);
            occurrences[currentJoin.Id] = await _recurringStore.GetRecurringOccurrenceAsync(
                    currentJoin.RecurringOccurrenceId,
                    cancellationToken) ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var boundaryJoin = lockedJoins
            .Where(x =>
                x.State is RecurringCommitmentOccurrenceState.Assigned or
                    RecurringCommitmentOccurrenceState.Confirmed &&
                occurrences[x.Id].LocalDate == requestedDate &&
                x.AssignmentId.HasValue)
            .OrderBy(x => x.Id)
            .FirstOrDefault();
        if (boundaryJoin is null)
        {
            throw new DomainException("Choose an included future occurrence with an active recurring assignment.");
        }

        var affectedJoins = lockedJoins
            .Where(x =>
                x.State is RecurringCommitmentOccurrenceState.Assigned or
                    RecurringCommitmentOccurrenceState.Confirmed &&
                x.AssignmentId.HasValue &&
                occurrences[x.Id].LocalDate >= requestedDate)
            .ToArray();
        if (affectedJoins.Length == 0)
        {
            throw new DomainException("Choose an included future occurrence with an active recurring assignment.");
        }

        var assignments = new Dictionary<Guid, Assignment>();
        foreach (var assignmentId in affectedJoins
                     .Select(x => x.AssignmentId!.Value)
                     .Distinct()
                     .OrderBy(x => x))
        {
            await _workflowStore.LockAssignmentAsync(assignmentId, cancellationToken);
        }

        foreach (var join in affectedJoins)
        {
            var assignment = await _workflowStore.GetAssignmentAsync(join.AssignmentId!.Value, cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            assignments[assignment.Id] = assignment;
        }

        foreach (var shiftId in assignments.Values.Select(x => x.ShiftId).Distinct().OrderBy(x => x))
        {
            await _workflowStore.LockShiftAsync(shiftId, cancellationToken);
        }

        foreach (var slotId in assignments.Values.Select(x => x.ShiftSlotId).Distinct().OrderBy(x => x))
        {
            await _workflowStore.LockSlotAsync(slotId, cancellationToken);
        }

        Guid? notificationSlotId = null;
        var cancelledCount = 0;
        foreach (var join in affectedJoins.OrderBy(x => x.Id))
        {
            var currentJoin = await _commitmentStore.GetRecurringCommitmentOccurrenceAsync(
                    commitment.Id,
                    join.RecurringOccurrenceId,
                    cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            if (currentJoin.State is not (RecurringCommitmentOccurrenceState.Assigned or
                    RecurringCommitmentOccurrenceState.Confirmed) ||
                !currentJoin.AssignmentId.HasValue)
            {
                throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            }

            var assignment = await _workflowStore.GetAssignmentAsync(
                    currentJoin.AssignmentId.Value,
                    cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            var shift = await RequireShiftAsync(assignment.ShiftId, cancellationToken);
            var occurrence = occurrences[join.Id];
            if (!shift.IsActive ||
                shift.StartsAtUtc <= nowUtc ||
                occurrence.IsException ||
                !assignment.IsActive)
            {
                throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            }

            assignment.Cancel(nowUtc);
            await InvalidateAssignmentAccessAsync(assignment, nowUtc, cancellationToken);
            currentJoin.MarkWithdrawn("Withdrawn from this future occurrence.");
            QueueNotification(
                assignment.Id,
                volunteer.Id,
                assignment.ShiftSlotId,
                $"assignment:{assignment.Id:N}:recurring-withdrawal",
                "RecurringCommitmentWithdrawal",
                nowUtc);
            notificationSlotId ??= assignment.ShiftSlotId;
            cancelledCount++;
        }

        commitment.Withdraw(requestedDate, nowUtc);
        capability.Invalidate(nowUtc);
        QueueNotification(
            commitment.Id,
            volunteer.Id,
            notificationSlotId,
            $"recurring:{commitment.Id:N}:withdrawn:{requestedDate:yyyyMMdd}",
            "RecurringCommitmentWithdrawn",
            nowUtc);
        _workflowStore.AddAuditEntry(AuditEntry.Create(
            nowUtc,
            "volunteer-token",
            "RecurringCommitmentWithdrawn",
            nameof(RecurringCommitment),
            commitment.Id,
            Detail(new { commitment.Id, EffectiveLocalDate = requestedDate, Cancelled = cancelledCount }),
            shiftId: assignments.Values.Select(x => (Guid?)x.ShiftId).First(),
            volunteerId: volunteer.Id));
        return ("Withdrawn", volunteer.Id);
    }

    public async Task<RecurringCommitmentHandoffPreviewDto> PreviewHandoffAsync(
        Guid commitmentId,
        DateOnly effectiveLocalDate,
        CancellationToken cancellationToken)
    {
        var commitment = await RequireCommitmentAsync(commitmentId, cancellationToken);
        var series = await RequireSeriesAsync(commitment.SeriesId, cancellationToken);
        if (effectiveLocalDate < commitment.EffectiveLocalDate ||
            effectiveLocalDate > commitment.EndLocalDate)
        {
            throw new DomainException("Choose a future date inside the recurring commitment.");
        }

        var rows = await BuildHandoffRowsAsync(
            commitment,
            effectiveLocalDate,
            null,
            cancellationToken);
        return new RecurringCommitmentHandoffPreviewDto(
            commitment.Id,
            commitment.SeriesId,
            effectiveLocalDate,
            rows,
            rows.Count(x => x.NewOccurrenceId.HasValue),
            rows.Count(x => !x.NewOccurrenceId.HasValue),
            SerializeHandoffMapping(
                rows,
                commitment.Id,
                commitment.Version,
                series.Id,
                series.Version,
                effectiveLocalDate),
            commitment.Version,
            series.Version);
    }
    public async Task<RecurringCommitmentHandoffPreviewDto> PreviewHandoffAsync(
        Guid commitmentId,
        DateOnly effectiveLocalDate,
        IReadOnlyDictionary<Guid, Guid> selectedMapping,
        CancellationToken cancellationToken)
    {
        var commitment = await RequireCommitmentAsync(commitmentId, cancellationToken);
        var series = await RequireSeriesAsync(commitment.SeriesId, cancellationToken);
        if (effectiveLocalDate < commitment.EffectiveLocalDate ||
            effectiveLocalDate > commitment.EndLocalDate)
        {
            throw new DomainException("Choose a future date inside the recurring commitment.");
        }

        var rows = await BuildHandoffRowsAsync(
            commitment,
            effectiveLocalDate,
            selectedMapping,
            cancellationToken);
        return new RecurringCommitmentHandoffPreviewDto(
            commitment.Id,
            commitment.SeriesId,
            effectiveLocalDate,
            rows,
            rows.Count(x => x.NewOccurrenceId.HasValue),
            rows.Count(x => !x.NewOccurrenceId.HasValue),
            SerializeHandoffMapping(
                rows,
                commitment.Id,
                commitment.Version,
                series.Id,
                series.Version,
                effectiveLocalDate),
            commitment.Version,
            series.Version);
    }

    public async Task ApplyHandoffAsync(
        Guid commitmentId,
        DateOnly effectiveLocalDate,
        string expectedMapping,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        var accessIntents = new List<MaterializedAccess>();
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _commitmentStore.LockRecurringCommitmentAsync(commitmentId, token);
                var commitment = await RequireCommitmentAsync(commitmentId, token);
                await _recurringStore.LockRecurringSeriesAsync(commitment.SeriesId, token);
                var parsed = ParseHandoffMapping(expectedMapping);
                var series = await RequireSeriesAsync(commitment.SeriesId, token);
                if (parsed.CommitmentId != commitment.Id ||
                    parsed.CommitmentVersion != commitment.Version ||
                    parsed.SeriesId != series.Id ||
                    parsed.SeriesVersion != series.Version ||
                    parsed.EffectiveLocalDate != effectiveLocalDate)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                var previewRows = await BuildHandoffRowsAsync(
                    commitment,
                    effectiveLocalDate,
                    parsed.Mapping,
                    token);
                if (!string.Equals(
                        expectedMapping,
                        SerializeHandoffMapping(
                            previewRows,
                            commitment.Id,
                            commitment.Version,
                            series.Id,
                            series.Version,
                            effectiveLocalDate),
                        StringComparison.Ordinal))
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                var moveRows = previewRows
                    .Where(x => x.NewOccurrenceId.HasValue)
                    .OrderBy(x => x.JoinId)
                    .ToArray();

                if (moveRows.Length == 0)
                {
                    throw new DomainException("Choose at least one eligible revised occurrence before confirming the handoff.");
                }

                foreach (var row in moveRows)
                {
                    var oldJoin = await _commitmentStore.GetRecurringCommitmentOccurrenceAsync(
                        commitment.Id,
                        row.OldOccurrenceId,
                        token) ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    await _commitmentStore.LockRecurringCommitmentOccurrenceAsync(oldJoin.Id, token);
                    await _recurringStore.LockRecurringOccurrenceAsync(row.OldOccurrenceId, token);
                    await _recurringStore.LockRecurringOccurrenceAsync(row.NewOccurrenceId!.Value, token);
                }

                var oldAssignments = new Dictionary<Guid, Assignment>();
                var targetSlots = new Dictionary<Guid, ShiftSlot>();
                var targetShifts = new Dictionary<Guid, Shift>();
                foreach (var row in moveRows)
                {
                    var oldJoin = await _commitmentStore.GetRecurringCommitmentOccurrenceAsync(
                            commitment.Id,
                            row.OldOccurrenceId,
                            token)
                        ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    if (!oldJoin.AssignmentId.HasValue)
                    {
                        throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    }

                    var oldAssignment = await _workflowStore.GetAssignmentAsync(oldJoin.AssignmentId.Value, token)
                        ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    oldAssignments[oldAssignment.Id] = oldAssignment;

                    var targetOccurrence = await _recurringStore.GetRecurringOccurrenceAsync(
                            row.NewOccurrenceId!.Value,
                            token)
                        ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    if (!targetOccurrence.ShiftId.HasValue)
                    {
                        throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    }

                    var targetShift = await RequireShiftAsync(targetOccurrence.ShiftId.Value, token);
                    var targetSlot = targetShift.Slots.FirstOrDefault(x =>
                        x.IsActive &&
                        x.Kind == commitment.RoleKind &&
                        x.Position == commitment.RolePosition);
                    if (targetSlot is null)
                    {
                        throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    }

                    targetShifts[targetShift.Id] = targetShift;
                    targetSlots[targetSlot.Id] = targetSlot;
                }

                foreach (var assignmentId in oldAssignments.Keys.OrderBy(x => x))
                {
                    await _workflowStore.LockAssignmentAsync(assignmentId, token);
                }

                foreach (var shiftId in oldAssignments.Values
                             .Select(x => x.ShiftId)
                             .Concat(targetShifts.Keys)
                             .Distinct()
                             .OrderBy(x => x))
                {
                    await _workflowStore.LockShiftAsync(shiftId, token);
                }

                foreach (var slotId in oldAssignments.Values
                             .Select(x => x.ShiftSlotId)
                             .Concat(targetSlots.Keys)
                             .Distinct()
                             .OrderBy(x => x))
                {
                    await _workflowStore.LockSlotAsync(slotId, token);
                }

                previewRows = await BuildHandoffRowsAsync(
                    commitment,
                    effectiveLocalDate,
                    parsed.Mapping,
                    token);
                if (!string.Equals(
                        expectedMapping,
                        SerializeHandoffMapping(
                            previewRows,
                            commitment.Id,
                            commitment.Version,
                            series.Id,
                            series.Version,
                            effectiveLocalDate),
                        StringComparison.Ordinal))
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                var volunteer = await RequireVolunteerAsync(commitment.VolunteerId, token);
                if (volunteer.AnonymizedAtUtc.HasValue)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                Guid? targetRevisionId = null;
                foreach (var row in previewRows.Where(x => x.NewOccurrenceId.HasValue).OrderBy(x => x.JoinId))
                {
                    var oldJoin = await _commitmentStore.GetRecurringCommitmentOccurrenceAsync(
                            commitment.Id,
                            row.OldOccurrenceId,
                            token)
                        ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    var oldOccurrence = await _recurringStore.GetRecurringOccurrenceAsync(
                            row.OldOccurrenceId,
                            token)
                        ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    var oldAssignment = oldJoin.AssignmentId is Guid oldAssignmentId
                        ? await _workflowStore.GetAssignmentAsync(oldAssignmentId, token)
                        : null;
                    var targetOccurrence = await _recurringStore.GetRecurringOccurrenceAsync(
                            row.NewOccurrenceId!.Value,
                            token)
                        ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    var targetShift = targetOccurrence.ShiftId is Guid targetShiftId
                        ? await RequireShiftAsync(targetShiftId, token)
                        : null;
                    var targetSlot = targetShift?.Slots.FirstOrDefault(x =>
                        x.IsActive &&
                        x.Kind == commitment.RoleKind &&
                        x.Position == commitment.RolePosition);
                    if (oldJoin.State is not (RecurringCommitmentOccurrenceState.Assigned or
                            RecurringCommitmentOccurrenceState.Confirmed) ||
                        oldAssignment is null ||
                        !oldAssignment.IsActive ||
                        oldOccurrence.IsException ||
                        targetShift is null ||
                        targetSlot is null ||
                        targetOccurrence.IsException ||
                        targetOccurrence.Status != RecurringOccurrenceStatus.Generated ||
                        targetShift.StartsAtUtc <= now ||
                        await _workflowStore.GetActiveAssignmentForSlotAsync(targetSlot.Id, token) is not null)
                    {
                        throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    }

                    if (targetRevisionId.HasValue && targetRevisionId.Value != targetOccurrence.RevisionId)
                    {
                        throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                    }

                    targetRevisionId ??= targetOccurrence.RevisionId;
                    oldAssignment.Cancel(now);
                    await InvalidateAssignmentAccessAsync(oldAssignment, now, token);
                    oldJoin.MarkReplaced("Moved to the revised recurring schedule.");
                    oldOccurrence.MarkException("Replaced by an explicit recurring handoff.");
                    var replacement = commitment.State == RecurringCommitmentState.Active
                        ? Assignment.DirectClaim(targetSlot.Id, targetShift.Id, volunteer.Id, now)
                        : Assignment.Create(targetSlot.Id, targetShift.Id, volunteer.Id, null, actor, now);
                    _workflowStore.AddAssignment(replacement);
                    _commitmentStore.AddRecurringCommitmentOccurrence(
                        RecurringCommitmentOccurrence.Create(
                            commitment.Id,
                            targetOccurrence.Id,
                            commitment.State == RecurringCommitmentState.Active
                                ? RecurringCommitmentOccurrenceState.Confirmed
                                : RecurringCommitmentOccurrenceState.Assigned,
                            replacement.Id));
                    accessIntents.Add(CreateAssignmentAccess(replacement, volunteer, targetSlot.Id, now));
                }

                if (targetRevisionId.HasValue)
                {
                    commitment.MoveToRevision(targetRevisionId.Value, now);
                }

                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringCommitmentHandedOff",
                    nameof(RecurringCommitment),
                    commitment.Id,
                    Detail(new
                    {
                        commitment.Id,
                        effectiveLocalDate,
                        Moved = previewRows.Count(x => x.NewOccurrenceId.HasValue),
                        Skipped = previewRows.Count(x => !x.NewOccurrenceId.HasValue)
                    }),
                    shiftId: targetShifts.Keys.Select(x => (Guid?)x).First(),
                    volunteerId: volunteer.Id));
                return true;
            },
            cancellationToken);

        foreach (var material in accessIntents)
        {
            PutHubToken(material.Intent, material.RawToken, now);
        }
    }
    private async Task<IReadOnlyList<RecurringCommitmentHandoffRowDto>> BuildHandoffRowsAsync(
        RecurringCommitment commitment,
        DateOnly effectiveLocalDate,
        IReadOnlyDictionary<Guid, Guid>? selectedMapping,
        CancellationToken cancellationToken)
    {
        var joins = await _commitmentStore.GetRecurringCommitmentOccurrencesAsync(
            commitment.Id,
            cancellationToken);
        var allOccurrences = await _recurringStore.GetRecurringOccurrencesAsync(
            commitment.SeriesId,
            effectiveLocalDate,
            commitment.EndLocalDate,
            cancellationToken);
        var targets = new Dictionary<Guid, HandoffTarget>();
        foreach (var occurrence in allOccurrences.Where(x =>
                     x.Status == RecurringOccurrenceStatus.Generated &&
                     !x.IsException &&
                     x.ShiftId.HasValue))
        {
            var shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId!.Value, cancellationToken);
            var slot = shift?.Slots.FirstOrDefault(x =>
                x.IsActive &&
                x.Kind == commitment.RoleKind &&
                x.Position == commitment.RolePosition);
            if (shift is null ||
                slot is null ||
                !shift.IsActive ||
                shift.StartsAtUtc <= _clock.UtcNow ||
                await _workflowStore.GetActiveAssignmentForSlotAsync(slot.Id, cancellationToken) is not null)
            {
                continue;
            }

            targets[occurrence.Id] = new HandoffTarget(occurrence, shift, slot);
        }

        var availableTargetIds = targets.Keys.OrderBy(x => x).ToArray();
        var usedTargetIds = new HashSet<Guid>();
        var rows = new List<RecurringCommitmentHandoffRowDto>();
        foreach (var join in joins.OrderBy(x => x.Id))
        {
            if (join.State is not (RecurringCommitmentOccurrenceState.Assigned or
                    RecurringCommitmentOccurrenceState.Confirmed) ||
                !join.AssignmentId.HasValue)
            {
                continue;
            }

            var oldOccurrence = await _recurringStore.GetRecurringOccurrenceAsync(
                join.RecurringOccurrenceId,
                cancellationToken);
            if (oldOccurrence is null || oldOccurrence.LocalDate < effectiveLocalDate)
            {
                continue;
            }

            var oldShift = oldOccurrence.ShiftId is Guid oldShiftId
                ? await _workflowStore.GetShiftAsync(oldShiftId, cancellationToken)
                : null;
            Guid? selectedTarget = null;
            var hasExplicitSelection = false;
            var explicitTarget = Guid.Empty;
            if (selectedMapping is not null)
            {
                hasExplicitSelection = selectedMapping.TryGetValue(join.Id, out explicitTarget);
                selectedTarget = explicitTarget == Guid.Empty ? null : explicitTarget;
            }
            else
            {
                selectedTarget = targets
                    .Where(x =>
                        x.Value.Occurrence.LocalDate == oldOccurrence.LocalDate &&
                        x.Key != oldOccurrence.Id &&
                        x.Value.Occurrence.RevisionId != oldOccurrence.RevisionId)
                    .Select(x => (Guid?)x.Key)
                    .FirstOrDefault();
            }

            HandoffTarget? target = null;
            var validTarget = false;
            if (selectedTarget.HasValue &&
                targets.TryGetValue(selectedTarget.Value, out var resolvedTarget) &&
                resolvedTarget.Occurrence.RevisionId != oldOccurrence.RevisionId &&
                usedTargetIds.Add(selectedTarget.Value))
            {
                target = resolvedTarget;
                validTarget = true;
            }

            if (!validTarget)
            {
                selectedTarget = null;
            }

            rows.Add(new RecurringCommitmentHandoffRowDto(
                join.Id,
                oldOccurrence.Id,
                oldOccurrence.LocalDate,
                selectedTarget,
                selectedTarget.HasValue ? target!.Occurrence.LocalDate : null,
                selectedTarget.HasValue ? "Move" : "Skipped",
                selectedTarget.HasValue
                    ? null
                    : hasExplicitSelection
                        ? "The selected revised occurrence is no longer eligible."
                        : "Choose an eligible revised occurrence before confirming the handoff.",
                availableTargetIds,
                join.Version,
                oldOccurrence.Version,
                oldOccurrence.RevisionId,
                oldShift?.Version,
                oldShift?.StartsAtUtc,
                selectedTarget.HasValue ? target!.Occurrence.Version : null,
                selectedTarget.HasValue ? target!.Occurrence.RevisionId : null,
                selectedTarget.HasValue ? target!.Shift.Version : null,
                selectedTarget.HasValue ? target!.Shift.StartsAtUtc : null));
        }

        return rows;
    }

    private static string SerializeHandoffMapping(
        IEnumerable<RecurringCommitmentHandoffRowDto> rows,
        Guid commitmentId,
        uint commitmentVersion,
        Guid seriesId,
        uint seriesVersion,
        DateOnly effectiveLocalDate) =>
        "meta:" +
        string.Join(
            ":",
            commitmentId.ToString("N"),
            commitmentVersion,
            seriesId.ToString("N"),
            seriesVersion,
            effectiveLocalDate.ToString("yyyy-MM-dd")) +
        "|" +
        string.Join(
            "|",
            rows.OrderBy(x => x.JoinId)
                .Select(x =>
                    $"{x.JoinId:N}={(x.NewOccurrenceId ?? Guid.Empty):N}:" +
                    $"{x.OldJoinVersion}:{x.OldOccurrenceVersion}:" +
                    $"{x.OldRevisionId?.ToString("N") ?? "-"}:" +
                    $"{x.OldShiftVersion?.ToString() ?? "-"}:" +
                    $"{(x.OldStartsAtUtc is DateTimeOffset oldStart ? oldStart.UtcDateTime.Ticks.ToString() : "-")}:" +
                    $"{x.NewOccurrenceVersion?.ToString() ?? "-"}:" +
                    $"{x.NewRevisionId?.ToString("N") ?? "-"}:" +
                    $"{x.NewShiftVersion?.ToString() ?? "-"}:" +
                    $"{(x.NewStartsAtUtc is DateTimeOffset newStart ? newStart.UtcDateTime.Ticks.ToString() : "-")}"));

    private static ParsedHandoffMapping ParseHandoffMapping(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !parts[0].StartsWith("meta:", StringComparison.Ordinal))
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var metadata = parts[0]["meta:".Length..].Split(':', StringSplitOptions.None);
        if (metadata.Length != 5 ||
            !Guid.TryParseExact(metadata[0], "N", out var commitmentId) ||
            !uint.TryParse(metadata[1], out var commitmentVersion) ||
            !Guid.TryParseExact(metadata[2], "N", out var seriesId) ||
            !uint.TryParse(metadata[3], out var seriesVersion) ||
            !DateOnly.TryParseExact(metadata[4], "yyyy-MM-dd", out var effectiveLocalDate))
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var result = new Dictionary<Guid, Guid>();
        foreach (var item in parts.Skip(1))
        {
            var mapping = item.Split('=', 2);
            var targetPart = mapping.Length == 2
                ? mapping[1].Split(':', 2)[0]
                : string.Empty;
            if (mapping.Length != 2 ||
                !Guid.TryParseExact(mapping[0], "N", out var joinId) ||
                !Guid.TryParseExact(targetPart, "N", out var targetId))
            {
                throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            }

            result[joinId] = targetId;
        }

        return new ParsedHandoffMapping(
            commitmentId,
            commitmentVersion,
            seriesId,
            seriesVersion,
            effectiveLocalDate,
            result);
    }

    private sealed record ParsedHandoffMapping(
        Guid CommitmentId,
        uint CommitmentVersion,
        Guid SeriesId,
        uint SeriesVersion,
        DateOnly EffectiveLocalDate,
        IReadOnlyDictionary<Guid, Guid> Mapping);

    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var commitments = await _commitmentStore.GetRecurringCommitmentsAsync(cancellationToken);
        foreach (var commitment in commitments.Where(x => x.IsOpenForOverlap))
        {
            await ReconcileCommitmentAsync(commitment.Id, cancellationToken);
        }
    }


    public async Task<IReadOnlyList<RecurringCommitmentDateDto>> GetCommitmentDatesAsync(
        Guid commitmentId,
        CancellationToken cancellationToken) =>
        await BuildCommitmentDateDtosAsync(
            await RequireCommitmentAsync(commitmentId, cancellationToken),
            cancellationToken);

    private async Task ReconcileCommitmentAsync(Guid commitmentId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var accessIntents = new List<MaterializedAccess>();
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _commitmentStore.LockRecurringCommitmentAsync(commitmentId, token);
                var commitment = await RequireCommitmentAsync(commitmentId, token);
                if (!commitment.IsOpenForOverlap)
                {
                    return true;
                }

                var revision = await RequireRevisionAsync(commitment.RevisionId, token);
                var candidates = await BuildCandidatesAsync(
                    commitment.SeriesId,
                    revision,
                    commitment.RoleKind,
                    commitment.RolePosition,
                    commitment.EffectiveLocalDate,
                    commitment.EndLocalDate,
                    token);
                var existing = (await _commitmentStore.GetRecurringCommitmentOccurrencesAsync(commitment.Id, token))
                    .ToDictionary(x => x.RecurringOccurrenceId);
                var volunteer = await RequireVolunteerAsync(commitment.VolunteerId, token);
                foreach (var candidate in candidates.OrderBy(x => x.Occurrence.Id))
                {
                    if (existing.ContainsKey(candidate.Occurrence.Id))
                    {
                        continue;
                    }

                    if (!candidate.Included)
                    {
                        var skipped = RecurringCommitmentOccurrence.Create(
                            commitment.Id,
                            candidate.Occurrence.Id,
                            RecurringCommitmentOccurrenceState.SkippedException,
                            reason: candidate.Reason);
                        _commitmentStore.AddRecurringCommitmentOccurrence(skipped);
                        continue;
                    }

                    await _workflowStore.LockSlotAsync(candidate.Slot!.Id, token);
                    if (await _workflowStore.GetActiveAssignmentForSlotAsync(candidate.Slot.Id, token) is not null)
                    {
                        var skipped = RecurringCommitmentOccurrence.Create(
                            commitment.Id,
                            candidate.Occurrence.Id,
                            RecurringCommitmentOccurrenceState.SkippedException,
                            reason: "Already filled by another assignment.");
                        _commitmentStore.AddRecurringCommitmentOccurrence(skipped);
                        continue;
                    }

                    var assignment = commitment.State == RecurringCommitmentState.Active
                        ? Assignment.DirectClaim(candidate.Slot.Id, candidate.Shift!.Id, volunteer.Id, now)
                        : Assignment.Create(candidate.Slot.Id, candidate.Shift!.Id, volunteer.Id, null, "RECURRING", now);
                    _workflowStore.AddAssignment(assignment);
                    var join = RecurringCommitmentOccurrence.Create(
                        commitment.Id,
                        candidate.Occurrence.Id,
                        commitment.State == RecurringCommitmentState.Active
                            ? RecurringCommitmentOccurrenceState.Confirmed
                            : RecurringCommitmentOccurrenceState.Assigned,
                        assignment.Id);
                    _commitmentStore.AddRecurringCommitmentOccurrence(join);
                    accessIntents.Add(CreateAssignmentAccess(assignment, volunteer, candidate.Slot.Id, now));
                }

                if (commitment.State == RecurringCommitmentState.Active &&
                    await GetFinalOccurrenceEndsAtUtcAsync(commitment, token) is DateTimeOffset finalEndsAtUtc &&
                    finalEndsAtUtc <= now)
                {
                    commitment.Complete(now);
                }

                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    "recurring-worker",
                    "RecurringCommitmentReconciled",
                    nameof(RecurringCommitment),
                    commitment.Id,
                    Detail(new { commitment.Id }),
                    shiftId: candidates.Where(x => x.Included && x.Shift is not null).Select(x => (Guid?)x.Shift!.Id).FirstOrDefault(),
                    volunteerId: volunteer.Id));
                return true;
            },
            cancellationToken);

        foreach (var material in accessIntents)
        {
            PutHubToken(material.Intent, material.RawToken, now);
        }
    }

    private async Task<IReadOnlyList<RecurringCommitmentOccurrence>> MaterializeAssignmentsAsync(
        RecurringCommitment commitment,
        IReadOnlyList<Candidate> candidates,
        Volunteer volunteer,
        DateTimeOffset now,
        bool confirmed,
        string actor,
        ICollection<MaterializedAccess> accessIntents,
        CancellationToken cancellationToken)
    {
        var joins = new List<RecurringCommitmentOccurrence>(candidates.Count);
        foreach (var candidate in candidates.OrderBy(x => x.Slot?.Id ?? Guid.Empty))
        {
            if (!candidate.Included)
            {
                var skipped = RecurringCommitmentOccurrence.Create(
                    commitment.Id,
                    candidate.Occurrence.Id,
                    RecurringCommitmentOccurrenceState.SkippedException,
                    reason: candidate.Reason);
                _commitmentStore.AddRecurringCommitmentOccurrence(skipped);
                joins.Add(skipped);
                continue;
            }

            await _workflowStore.LockSlotAsync(candidate.Slot!.Id, cancellationToken);
            if (await _workflowStore.GetActiveAssignmentForSlotAsync(candidate.Slot.Id, cancellationToken) is not null)
            {
                var skipped = RecurringCommitmentOccurrence.Create(
                    commitment.Id,
                    candidate.Occurrence.Id,
                    RecurringCommitmentOccurrenceState.SkippedException,
                    reason: "Already filled by another assignment.");
                _commitmentStore.AddRecurringCommitmentOccurrence(skipped);
                joins.Add(skipped);
                continue;
            }

            var assignment = confirmed
                ? Assignment.DirectClaim(candidate.Slot.Id, candidate.Shift!.Id, volunteer.Id, now)
                : Assignment.Create(candidate.Slot.Id, candidate.Shift!.Id, volunteer.Id, null, actor, now);
            _workflowStore.AddAssignment(assignment);
            var join = RecurringCommitmentOccurrence.Create(
                commitment.Id,
                candidate.Occurrence.Id,
                confirmed
                    ? RecurringCommitmentOccurrenceState.Confirmed
                    : RecurringCommitmentOccurrenceState.Assigned,
                assignment.Id);
            _commitmentStore.AddRecurringCommitmentOccurrence(join);
            joins.Add(join);
            accessIntents.Add(CreateAssignmentAccess(assignment, volunteer, candidate.Slot.Id, now));
        }

        return joins;
    }

    private MaterializedAccess CreateAssignmentAccess(
        Assignment assignment,
        Volunteer volunteer,
        Guid slotId,
        DateTimeOffset now)
    {
        var generated = _tokens.Generate();
        if (_workflowStore is not IAccessStore accessStore)
        {
            throw new DomainException("Volunteer access is unavailable.");
        }

        accessStore.AddCapability(VolunteerAccessCapability.Create(
            slotId,
            volunteer.Id,
            generated.Hash,
            now,
            CapabilityIssuedReason.DirectAssignment));
        var intent = QueueNotification(
            assignment.Id,
            volunteer.Id,
            slotId,
            $"assignment:{assignment.Id:N}:access",
            "AssignmentAccess",
            now);
        return new MaterializedAccess(intent, generated.RawToken);
    }

    private async Task<IReadOnlyList<Candidate>> BuildCandidatesAsync(
        Guid seriesId,
        RecurringShiftSeriesRevision revision,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        DateOnly endLocalDate,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var occurrences = await _recurringStore.GetRecurringOccurrencesAsync(
            seriesId,
            effectiveLocalDate,
            endLocalDate,
            cancellationToken);
        var candidates = new List<Candidate>(occurrences.Count);
        foreach (var occurrence in occurrences.OrderBy(x => x.LocalDate).ThenBy(x => x.Id))
        {
            Shift? shift = occurrence.ShiftId.HasValue
                ? await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, cancellationToken)
                : null;
            var slot = shift?.Slots.FirstOrDefault(x =>
                x.IsActive && x.Kind == roleKind && x.Position == rolePosition);
            var activeAssignment = slot is null
                ? null
                : await _workflowStore.GetActiveAssignmentForSlotAsync(slot.Id, cancellationToken);
            var startsAt = shift?.StartsAtUtc ?? ResolveDisplayStart(revision, occurrence.LocalDate);
            var reason = CandidateReason(occurrence, shift, slot, now);
            if (reason is null && activeAssignment is not null)
            {
                reason = "Already filled by another assignment.";
            }
            candidates.Add(new Candidate(
                occurrence,
                shift,
                slot,
                startsAt,
                reason is null,
                reason is null ? "Included" : "Skipped",
                reason));
        }

        return candidates;
    }

    private async Task<IReadOnlyList<RecurringCommitmentDateDto>> BuildRequestDateDtosAsync(
        RecurringCommitmentRequest request,
        RecurringShiftSeriesRevision revision,
        CancellationToken cancellationToken)
    {
        var candidates = await BuildCandidatesAsync(
            request.SeriesId,
            revision,
            request.RoleKind,
            request.RolePosition,
            request.EffectiveLocalDate,
            request.EndLocalDate,
            cancellationToken);
        return candidates.Select(x => new RecurringCommitmentDateDto(
            Guid.Empty,
            x.Occurrence.Id,
            x.Occurrence.LocalDate,
            x.StartsAtUtc,
            RoleLabel(request.RoleKind, request.RolePosition),
            x.State,
            x.Reason,
            null)).ToArray();
    }

    private async Task<IReadOnlyList<RecurringCommitmentDateDto>> BuildCommitmentDateDtosAsync(
        RecurringCommitment commitment,
        CancellationToken cancellationToken)
    {
        var joins = await _commitmentStore.GetRecurringCommitmentOccurrencesAsync(commitment.Id, cancellationToken);
        var result = new List<RecurringCommitmentDateDto>(joins.Count);
        foreach (var join in joins.OrderBy(x => x.Id))
        {
            var occurrence = await _recurringStore.GetRecurringOccurrenceAsync(join.RecurringOccurrenceId, cancellationToken);
            var starts = occurrence?.ShiftId is Guid shiftId
                ? (await _workflowStore.GetShiftAsync(shiftId, cancellationToken))?.StartsAtUtc ?? DateTimeOffset.MinValue
                : DateTimeOffset.MinValue;
            result.Add(new RecurringCommitmentDateDto(
                join.Id,
                join.RecurringOccurrenceId,
                occurrence?.LocalDate ?? commitment.EffectiveLocalDate,

                starts,
                RoleLabel(commitment.RoleKind, commitment.RolePosition),
                join.State.ToString(),
                join.Reason,
                join.AssignmentId));
        }

        return result;
    }
    private async Task<DateTimeOffset?> GetFinalOccurrenceEndsAtUtcAsync(
        RecurringCommitment commitment,
        CancellationToken cancellationToken)
    {
        var occurrences = await _recurringStore.GetRecurringOccurrencesAsync(
            commitment.SeriesId,
            commitment.EffectiveLocalDate,
            commitment.EndLocalDate,
            cancellationToken);
        DateTimeOffset? finalEndsAtUtc = null;
        foreach (var occurrence in occurrences)
        {
            if (occurrence.ShiftId is not Guid shiftId)
            {
                continue;
            }

            var shift = await _workflowStore.GetShiftAsync(shiftId, cancellationToken);
            if (shift is not null &&
                (!finalEndsAtUtc.HasValue || shift.EndsAtUtc > finalEndsAtUtc.Value))
            {
                finalEndsAtUtc = shift.EndsAtUtc;
            }
        }

        return finalEndsAtUtc;
    }


    private async Task<RecurringCommitmentCapability?> GetCapabilityAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        return await _commitmentStore.GetRecurringCommitmentCapabilityByHashAsync(
            _tokens.Hash(rawToken),
            cancellationToken);
    }

    private async Task ResolveRecurringRequestAccessAsync(
        RecurringCommitmentRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var capability in await _commitmentStore.GetActiveRecurringCapabilitiesAsync(
                     request.VolunteerId,
                     null,
                     cancellationToken))
        {
            if (capability.RequestId == request.Id)
            {
                capability.Invalidate(nowUtc);
            }
        }

        if (_workflowStore is not INotificationOutboxStore outbox)
        {
            return;
        }

        foreach (var intent in await outbox.GetNotificationIntentsForVolunteerAsync(
                     request.VolunteerId,
                     cancellationToken))
        {
            if (intent.TransitionId == request.Id &&
                intent.Kind == "RecurringCommitmentRequest" &&
                intent.State is NotificationIntentState.Pending or
                    NotificationIntentState.RetryScheduled or
                    NotificationIntentState.InFlight)
            {
                intent.Cancel(nowUtc, "CommitmentNoLongerEligible");
            }
        }
    }

    private async Task InvalidateAssignmentAccessAsync(
        Assignment assignment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_workflowStore is not IAccessStore accessStore)
        {
            return;
        }

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

    private NotificationIntent? QueueNotification(
        Guid transitionId,
        Guid volunteerId,
        Guid? slotId,
        string eventKey,
        string kind,
        DateTimeOffset nowUtc)
    {
        if (_workflowStore is not INotificationOutboxStore outbox)
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

    private void PutHubToken(NotificationIntent? intent, string rawToken, DateTimeOffset nowUtc)
    {
        if (intent is not null)
        {
            _transientLinkMaterial?.PutHubToken(intent.Id, rawToken, nowUtc.AddMinutes(15));
        }
    }

    private static string? CandidateReason(
        RecurringShiftOccurrence occurrence,
        Shift? shift,
        ShiftSlot? slot,
        DateTimeOffset nowUtc)
    {
        if (occurrence.Status == RecurringOccurrenceStatus.NeedsReview)
        {
            return "Needs review";
        }

        if (occurrence.Status == RecurringOccurrenceStatus.Skipped)
        {
            return occurrence.ResolutionReason ?? "Skipped by the coordinator";
        }

        if (occurrence.IsException)
        {
            return occurrence.ResolutionReason ?? "Manual exception";
        }

        if (shift is null)
        {
            return "Concrete occurrence is unavailable";
        }

        if (!shift.IsActive || shift.EndsAtUtc <= nowUtc)
        {
            return "Inactive or ended occurrence";
        }
        if (shift.StartsAtUtc <= nowUtc)
        {
            return "Occurrence has already started";
        }

        if (!shift.PublishedAtUtc.HasValue)
        {
            return "Not published";
        }

        if (slot is null)
        {
            return "Role is not available on this occurrence";
        }

        return null;
    }

    private static DateTimeOffset ResolveDisplayStart(
        RecurringShiftSeriesRevision revision,
        DateOnly localDate)
    {
        var resolution = RecurringLocalTimeResolver.Resolve(
            revision.TimeZoneId,
            localDate,
            revision.LocalStartTime,
            revision.AmbiguousTimeChoice);
        return resolution.StartsAtUtc ?? DateTimeOffset.MinValue;
    }

    private static RecurringCommitmentDateDto ToDateDto(Candidate candidate) => new(
        Guid.Empty,
        candidate.Occurrence.Id,
        candidate.Occurrence.LocalDate,
        candidate.StartsAtUtc,
        candidate.Slot is null ? "Role unavailable" : RoleLabel(candidate.Slot.Kind, candidate.Slot.Position),
        candidate.State,
        candidate.Reason,
        null);

    private async Task<RecurringShiftSeries> RequireSeriesAsync(
        Guid seriesId,
        CancellationToken cancellationToken) =>
        await _recurringStore.GetRecurringSeriesAsync(seriesId, cancellationToken)
        ?? throw new DomainException("The recurring series was not found.");

    private async Task<RecurringShiftSeriesRevision> RequireCurrentRevisionAsync(
        RecurringShiftSeries series,
        CancellationToken cancellationToken) =>
        await RequireRevisionAsync(series.CurrentRevisionNumber, series.Id, cancellationToken);

    private async Task<RecurringShiftSeriesRevision> RequireRevisionAsync(
        Guid revisionId,
        CancellationToken cancellationToken) =>
        await _recurringStore.GetRecurringRevisionAsync(revisionId, cancellationToken)
        ?? throw new DomainException("The recurring series revision was not found.");

    private async Task<RecurringShiftSeriesRevision> RequireRevisionAsync(
        int revisionNumber,
        Guid seriesId,
        CancellationToken cancellationToken) =>
        (await _recurringStore.GetRecurringRevisionsAsync(seriesId, cancellationToken))
            .SingleOrDefault(x => x.RevisionNumber == revisionNumber)
        ?? throw new DomainException("The recurring series revision was not found.");

    private async Task<RecurringCommitmentRequest> RequireRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken) =>
        await _commitmentStore.GetRecurringCommitmentRequestAsync(requestId, cancellationToken)
        ?? throw new DomainException("The recurring request was not found.");

    private async Task<RecurringCommitment> RequireCommitmentAsync(
        Guid commitmentId,
        CancellationToken cancellationToken) =>
        await _commitmentStore.GetRecurringCommitmentAsync(commitmentId, cancellationToken)
        ?? throw new DomainException("The recurring commitment was not found.");

    private async Task<Volunteer> RequireVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken) =>
        await _workflowStore.GetVolunteerAsync(volunteerId, cancellationToken)
        ?? throw new DomainException("The volunteer was not found.");

    private async Task<Shift> RequireShiftAsync(Guid shiftId, CancellationToken cancellationToken) =>
        await _workflowStore.GetShiftAsync(shiftId, cancellationToken)
        ?? throw new DomainException("The shift was not found.");

    private async Task<ShiftSlot> RequireSlotAsync(Guid slotId, CancellationToken cancellationToken) =>
        await _workflowStore.GetSlotAsync(slotId, cancellationToken)
        ?? throw new DomainException("The shift slot was not found.");

    private static DomainException InvalidHub() =>
        new("This recurring commitment link is invalid, expired, or no longer available.");

    private static string RequireCoordinator(string coordinatorEmail)
    {
        if (string.IsNullOrWhiteSpace(coordinatorEmail))
        {
            throw new DomainException("A coordinator identity is required.");
        }

        return coordinatorEmail.Trim().ToUpperInvariant();
    }

    private static string PolicyConsequence(SignupPolicy policy) => policy switch
    {
        SignupPolicy.DirectClaim => "Direct claim is lower maintenance: the first eligible volunteer is confirmed immediately.",
        _ => "Approval required is the safer default: a coordinator reviews the recurring request before assignment."
    };

    private static string RoleLabel(SlotKind kind, int position) => kind switch
    {
        SlotKind.Primary => "Primary",
        SlotKind.Backup when position == 1 => "Backup 1",
        SlotKind.Backup => $"Backup {position}",
        _ => "Role"
    };

    private static void ValidateRole(SlotKind roleKind, int rolePosition)
    {
        if (roleKind == SlotKind.Primary && rolePosition != 1 ||
            roleKind == SlotKind.Backup && rolePosition is < 1 or > 2)
        {
            throw new DomainException("Choose an available recurring role.");
        }
    }

    private static void ValidateHorizon(int horizonWeeks)
    {
        if (horizonWeeks is < MinimumHorizonWeeks or > MaximumHorizonWeeks)
        {
            throw new DomainException("A recurring commitment horizon must be between four and twenty-six weeks.");
        }
    }

    private static string Detail(object value) => JsonSerializer.Serialize(value);

    private static DateOnly EndDate(DateOnly effectiveLocalDate, int horizonWeeks) =>
        effectiveLocalDate.AddDays(horizonWeeks * 7 - 1);
}
