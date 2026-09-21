using System.Globalization;
using System.Text.Json;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Application.Time;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Auditing;
using VolunteerCoordinator.Domain.Assignments;
using VolunteerCoordinator.Domain.Requests;
using VolunteerCoordinator.Domain.Schedules;
using VolunteerCoordinator.Domain.Settings;

namespace VolunteerCoordinator.Application;

public sealed class RecurringShiftService
{
    public const int DefaultHorizonWeeks = 12;
    public const int MinimumHorizonWeeks = 4;
    public const int MaximumHorizonWeeks = 26;
    public const int MaximumGenerationWeeks = 52;
    private const string ProtectedExceptionReason = "Protected commitment retained as an occurrence exception.";

    private readonly IWorkflowStore _workflowStore;
    private readonly IRecurringShiftStore _recurringStore;
    private readonly IClock _clock;
    private readonly VolunteerCoordinatorService? _workflowCommands;


    public RecurringShiftService(
        IWorkflowStore workflowStore,
        IRecurringShiftStore recurringStore,
        IClock clock,
        VolunteerCoordinatorService? workflowCommands = null)
    {
        _workflowStore = workflowStore;
        _recurringStore = recurringStore;
        _clock = clock;
        _workflowCommands = workflowCommands;
    }
    public async Task<GroupSettingsDto?> GetGroupSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _workflowStore.GetGroupSettingsAsync(cancellationToken);
        return settings is null ? null : new GroupSettingsDto(settings.TimeZoneId, settings.Version);
    }


    public async Task<RecurringSeriesPreviewDto> PreviewRecurringSeriesAsync(
        RecurringSeriesInput input,
        CancellationToken cancellationToken)
    {
        var settings = await RequireSettingsAsync(cancellationToken);
        ValidateSettingsVersion(settings, input.ExpectedSettingsVersion);
        var revision = BuildRevision(
            Guid.NewGuid(),
            1,
            input,
            input.AnchorLocalDate,
            settings.TimeZoneId,
            "preview",
            _clock.UtcNow);
        return BuildPreview(revision, settings.TimeZoneId, input.ExpectedSettingsVersion, null, input.HorizonWeeks);
    }

    public async Task<Guid> CreateRecurringSeriesAsync(
        RecurringSeriesInput input,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        return await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _workflowStore.LockGroupSettingsAsync(token);
                var settings = await RequireSettingsAsync(token);
                ValidateSettingsVersion(settings, input.ExpectedSettingsVersion);
                var series = RecurringShiftSeries.Create(now);
                var revision = BuildRevision(
                    series.Id,
                    1,
                    input,
                    input.AnchorLocalDate,
                    settings.TimeZoneId,
                    actor,
                    now);
                series.AddRevision(revision, now);
                _recurringStore.AddRecurringSeries(series);
                _recurringStore.AddRecurringRevision(revision);
                await GenerateForRangeAsync(
                    series,
                    [revision],
                    revision.AnchorLocalDate,
                    revision.AnchorLocalDate.AddDays(revision.HorizonWeeks * 7),
                    actor,
                    token);
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringSeriesCreated",
                    nameof(RecurringShiftSeries),
                    series.Id,
                    Detail(new
                    {
                        series.Id,
                        RevisionId = revision.Id,
                        revision.RevisionNumber,
                        revision.RecurrenceKind,
                        revision.Interval,
                        revision.HorizonWeeks,
                        revision.TimeZoneId,
                        revision.SignupPolicy
                    })));
                return series.Id;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<RecurringSeriesSummaryDto>> ListRecurringSeriesAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _recurringStore.GetRecurringSeriesAsync(cancellationToken);
        var groupSettings = await RequireSettingsAsync(cancellationToken);
        var allShifts = (await _workflowStore.GetAllShiftsAsync(cancellationToken)).ToDictionary(x => x.Id);
        var summaries = new List<RecurringSeriesSummaryDto>(settings.Count);
        foreach (var series in settings)
        {
            var revisions = await _recurringStore.GetRecurringRevisionsAsync(series.Id, cancellationToken);
            var current = revisions.LastOrDefault(x => x.RevisionNumber == series.CurrentRevisionNumber)
                ?? revisions.LastOrDefault();
            if (current is null)
            {
                continue;
            }

            var occurrences = await _recurringStore.GetRecurringOccurrencesAsync(series.Id, null, null, cancellationToken);
            var needsReview = occurrences.Count(x => x.Status == RecurringOccurrenceStatus.NeedsReview);
            var unpublished = occurrences.Count(x =>
                x.Status == RecurringOccurrenceStatus.Generated &&
                x.ShiftId.HasValue &&
                allShifts.TryGetValue(x.ShiftId.Value, out var shift) &&
                !shift.PublishedAtUtc.HasValue);
            var protectedExceptions = occurrences.Count(x => x.IsException && x.Status == RecurringOccurrenceStatus.Generated);
            var skipped = occurrences.Count(x => x.Status == RecurringOccurrenceStatus.Skipped);
            summaries.Add(new RecurringSeriesSummaryDto(
                series.Id,
                series.IsActive,
                series.CurrentRevisionNumber,
                current.Title,
                RecurrenceDescription(current),
                current.TimeZoneId,
                needsReview,
                unpublished,
                protectedExceptions,
                skipped,
                !string.Equals(current.TimeZoneId, groupSettings.TimeZoneId, StringComparison.Ordinal),
                series.Version,
                current.SignupPolicy));
        }

        return summaries;
    }

    public async Task<RecurringSeriesDetailDto> GetRecurringSeriesDetailAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        var settings = await RequireSettingsAsync(cancellationToken);
        var series = await RequireSeriesAsync(seriesId, cancellationToken);
        var revisions = await _recurringStore.GetRecurringRevisionsAsync(seriesId, cancellationToken);
        var current = revisions.LastOrDefault(x => x.RevisionNumber == series.CurrentRevisionNumber)
            ?? throw new DomainException("The recurring series revision was not found.");
        var occurrences = await _recurringStore.GetRecurringOccurrencesAsync(seriesId, null, null, cancellationToken);
        var previews = await BuildOccurrencePreviewsAsync(occurrences, revisions, settings.TimeZoneId, cancellationToken);
        var summary = new RecurringSeriesSummaryDto(
            series.Id,
            series.IsActive,
            series.CurrentRevisionNumber,
            current.Title,
            RecurrenceDescription(current),
            current.TimeZoneId,
            previews.Count(x => x.Status == nameof(RecurringOccurrenceStatus.NeedsReview)),
            previews.Count(x => x.Status == nameof(RecurringOccurrenceStatus.Generated) && !x.IsPublished),
            previews.Count(x => x.IsException && x.Status == nameof(RecurringOccurrenceStatus.Generated)),
            previews.Count(x => x.Status == nameof(RecurringOccurrenceStatus.Skipped)),
            !string.Equals(current.TimeZoneId, settings.TimeZoneId, StringComparison.Ordinal),
            series.Version,
            current.SignupPolicy);
        return new RecurringSeriesDetailDto(
            summary,
            previews.Where(x => x.Status == nameof(RecurringOccurrenceStatus.NeedsReview)).ToArray(),
            previews.Where(x => x.Status == nameof(RecurringOccurrenceStatus.Generated) && !x.IsPublished).ToArray(),
            previews.Where(x => x.Status == nameof(RecurringOccurrenceStatus.Generated) && x.IsPublished).ToArray(),
            previews.Where(x => x.IsException && x.Status == nameof(RecurringOccurrenceStatus.Generated)).ToArray(),
            previews.Where(x => x.Status == nameof(RecurringOccurrenceStatus.Skipped)).ToArray(),
            series.Version);
    }
    public async Task<RecurringOccurrenceReviewDto> GetOccurrenceReviewAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        var occurrence = await RequireOccurrenceAsync(occurrenceId, cancellationToken);
        var revision = await RequireRevisionAsync(occurrence.RevisionId, cancellationToken);
        Shift? shift = occurrence.ShiftId.HasValue
            ? await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, cancellationToken)
            : null;
        var preview = BuildOccurrencePreview(occurrence, revision, shift, revision.TimeZoneId);
        return new RecurringOccurrenceReviewDto(
            occurrence.Id,
            occurrence.SeriesId,
            occurrence.LocalDate,
            revision.LocalStartTime,
            revision.TimeZoneId,
            revision.DurationMinutes,
            preview.Status,
            preview.DstStatus,
            occurrence.Version,
            occurrence.ResolutionReason);
    }

    public async Task<RecurringProtectedResolutionPreviewDto> PreviewProtectedResolutionAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        var occurrence = await RequireOccurrenceAsync(occurrenceId, cancellationToken);
        var series = await RequireSeriesAsync(occurrence.SeriesId, cancellationToken);
        if (!occurrence.IsException || !occurrence.ShiftId.HasValue)
        {
            throw new DomainException("This occurrence does not have a protected concrete commitment to resolve.");
        }

        var shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, cancellationToken)
            ?? throw new DomainException("The protected occurrence is missing its concrete shift.");
        var slotIds = shift.Slots.Where(x => x.IsActive).Select(x => x.Id).ToArray();
        var requests = await _workflowStore.GetPendingRequestsAsync(slotIds, cancellationToken);
        var assignments = await _workflowStore.GetActiveAssignmentsAsync(slotIds, cancellationToken);
        var people = await _workflowStore.GetVolunteersByIdsAsync(
            requests.Select(x => x.VolunteerId).Concat(assignments.Select(x => x.VolunteerId)).Distinct().ToArray(),
            cancellationToken);
        return new RecurringProtectedResolutionPreviewDto(
            occurrence.Id,
            occurrence.SeriesId,
            occurrence.LocalDate,
            people.Select(x => x.AnonymizedAtUtc.HasValue ? "Removed volunteer" : x.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            requests.Count,
            assignments.Count,
            occurrence.Version,
            series.Version);
    }

    public async Task ResolveProtectedOccurrenceAsync(
        Guid occurrenceId,
        uint expectedOccurrenceVersion,
        uint expectedSeriesVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        if (_workflowCommands is null)
        {
            throw new DomainException("Protected occurrence resolution is unavailable.");
        }

        var preview = await PreviewProtectedResolutionAsync(occurrenceId, cancellationToken);
        if (preview.ExpectedOccurrenceVersion != expectedOccurrenceVersion ||
            preview.ExpectedSeriesVersion != expectedSeriesVersion)
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var occurrence = await RequireOccurrenceAsync(occurrenceId, cancellationToken);
        var shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId!.Value, cancellationToken)
            ?? throw new DomainException("The protected occurrence is missing its concrete shift.");
        var slotIds = shift.Slots.Where(x => x.IsActive).Select(x => x.Id).ToArray();
        var requests = await _workflowStore.GetPendingRequestsAsync(slotIds, cancellationToken);
        var assignments = await _workflowStore.GetActiveAssignmentsAsync(slotIds, cancellationToken);
        foreach (var assignment in assignments.OrderBy(x => x.Id))
        {
            await _workflowCommands.CancelAssignmentAsync(assignment.Id, actor, cancellationToken);
        }

        foreach (var request in requests.OrderBy(x => x.Id))
        {
            await _workflowCommands.RejectRequestAsync(request.Id, actor, cancellationToken);
        }

        var now = _clock.UtcNow;
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _recurringStore.LockRecurringSeriesAsync(occurrence.SeriesId, token);
                await _recurringStore.LockRecurringOccurrenceAsync(occurrenceId, token);
                var currentSeries = await RequireSeriesAsync(occurrence.SeriesId, token);
                var currentOccurrence = await RequireOccurrenceAsync(occurrenceId, token);
                if (currentSeries.Version != expectedSeriesVersion ||
                    currentOccurrence.Version != expectedOccurrenceVersion ||
                    !currentOccurrence.ShiftId.HasValue)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                var revision = await RequireCurrentRevisionAsync(currentSeries, token);
                var resolution = RecurringLocalTimeResolver.Resolve(
                    revision.TimeZoneId,
                    currentOccurrence.LocalDate,
                    revision.LocalStartTime,
                    revision.AmbiguousTimeChoice);
                if (!resolution.IsResolved)
                {
                    throw new DomainException("The replacement revision has a local clock gap that needs review first.");
                }

                var currentShift = await _workflowStore.GetShiftAsync(currentOccurrence.ShiftId.Value, token)
                    ?? throw new DomainException("The protected occurrence is missing its concrete shift.");
                currentShift.Edit(
                    revision.Title,
                    revision.Location,
                    revision.InternalCoordinatorNotes,
                    revision.VolunteerInstructions,
                    resolution.StartsAtUtc!.Value,
                    resolution.StartsAtUtc.Value.AddMinutes(revision.DurationMinutes),
                    now);
                var existingSlotIds = currentShift.Slots.Select(x => x.Id).ToHashSet();
                currentShift.ConfigureBackupSlots(revision.BackupSlotCount);
                _workflowStore.AddShiftSlots(currentShift.Slots.Where(x => !existingSlotIds.Contains(x.Id)).ToArray());
                currentOccurrence.ApplyRevision(revision.Id);
                currentOccurrence.MarkException("Protected commitment explicitly resolved and replaced.");
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringProtectedOccurrenceResolved",
                    nameof(RecurringShiftOccurrence),
                    currentOccurrence.Id,
                    Detail(new { currentOccurrence.SeriesId, currentOccurrence.LocalDate, RevisionId = revision.Id }),
                    shiftId: currentOccurrence.ShiftId));
                return true;
            },
            cancellationToken);
    }


    public async Task<RecurringRevisionInput> GetCurrentRevisionInputAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        var series = await RequireSeriesAsync(seriesId, cancellationToken);
        var revision = await RequireCurrentRevisionAsync(series, cancellationToken);
        var settings = await RequireSettingsAsync(cancellationToken);
        return new RecurringRevisionInput(
            revision.EffectiveLocalDate,
            revision.Title,
            revision.Location,
            revision.VolunteerInstructions,
            revision.InternalCoordinatorNotes,
            revision.RecurrenceKind,
            revision.Interval,
            RecurringCalendar.ToWeekdays(revision.WeeklyDays),
            revision.AnchorLocalDate,
            revision.LocalStartTime,
            revision.DurationMinutes,
            revision.BackupSlotCount,
            revision.HorizonWeeks,
            revision.TimeZoneId,
            revision.AmbiguousTimeChoice,
            series.Version,
            null,
            settings.Version,
            revision.SignupPolicy);
    }


    public async Task<bool> GenerateSeriesAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        return await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _workflowStore.LockGroupSettingsAsync(token);
                await _recurringStore.LockRecurringSeriesAsync(seriesId, token);
                var series = await RequireSeriesAsync(seriesId, token);
                if (!series.IsActive)
                {
                    return false;
                }

                var settings = await RequireSettingsAsync(token);
                var revisions = await _recurringStore.GetRecurringRevisionsAsync(seriesId, token);
                var current = revisions.LastOrDefault(x => x.RevisionNumber == series.CurrentRevisionNumber)
                    ?? throw new DomainException("The recurring series revision was not found.");
                if (!string.Equals(current.TimeZoneId, settings.TimeZoneId, StringComparison.Ordinal))
                {
                    return false;
                }

                var groupToday = LocalToday(settings.TimeZoneId, now);
                var through = groupToday.AddDays(current.HorizonWeeks * 7);
                var from = revisions.Min(x => x.AnchorLocalDate) > groupToday
                    ? revisions.Min(x => x.AnchorLocalDate)
                    : groupToday;
                await GenerateForRangeAsync(series, revisions, from, through, "recurring-worker", token);
                series.MarkGeneratedThrough(through, now);
                return true;
            },
            cancellationToken);
    }
    public async Task<bool> GenerateSeriesThroughAsync(
        Guid seriesId,
        DateOnly requestedThroughLocalDate,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        return await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _workflowStore.LockGroupSettingsAsync(token);
                await _recurringStore.LockRecurringSeriesAsync(seriesId, token);
                var series = await RequireSeriesAsync(seriesId, token);
                var settings = await RequireSettingsAsync(token);
                var today = LocalToday(settings.TimeZoneId, now);
                if (requestedThroughLocalDate > today.AddDays(MaximumGenerationWeeks * 7))
                {
                    throw new DomainException("A recurring generation request cannot exceed fifty-two weeks.");
                }

                var revisions = await _recurringStore.GetRecurringRevisionsAsync(seriesId, token);
                var current = revisions.LastOrDefault(x => x.RevisionNumber == series.CurrentRevisionNumber)
                    ?? throw new DomainException("The recurring series revision was not found.");
                if (!series.IsActive ||
                    !string.Equals(current.TimeZoneId, settings.TimeZoneId, StringComparison.Ordinal))
                {
                    return false;
                }

                var from = revisions.Min(x => x.AnchorLocalDate) > today
                    ? revisions.Min(x => x.AnchorLocalDate)
                    : today;
                await GenerateForRangeAsync(series, revisions, from, requestedThroughLocalDate, "coordinator", token);
                series.MarkGeneratedThrough(requestedThroughLocalDate, now);
                return true;
            },
            cancellationToken);
    }


    public async Task<int> GenerateDueSeriesAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        foreach (var seriesId in await _recurringStore.GetActiveRecurringSeriesIdsAsync(cancellationToken))
        {
            try
            {
                if (await GenerateSeriesAsync(seriesId, cancellationToken))
                {
                    processed++;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // One malformed or concurrently changed series must not stop the ordered batch.
            }
        }

        return processed;
    }

    public async Task ResolveOccurrenceGapAsync(
        Guid occurrenceId,
        uint expectedVersion,
        TimeOnly replacementLocalStart,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _recurringStore.LockRecurringOccurrenceAsync(occurrenceId, token);
                var occurrence = await RequireOccurrenceAsync(occurrenceId, token);
                if (occurrence.Version != expectedVersion)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                var revision = await RequireRevisionAsync(occurrence.RevisionId, token);
                var resolution = RecurringLocalTimeResolver.Resolve(
                    revision.TimeZoneId,
                    occurrence.LocalDate,
                    replacementLocalStart,
                    revision.AmbiguousTimeChoice);
                if (!resolution.IsResolved)
                {
                    throw new DomainException("Choose a valid local replacement time outside the clock gap.");
                }

                occurrence.Resolve(replacementLocalStart, actor, now);
                var shift = CreateShiftFromRevision(revision, resolution.StartsAtUtc!.Value, occurrence.Id);
                _workflowStore.AddShift(shift);
                occurrence.AttachShift(shift.Id);
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringOccurrenceGapResolved",
                    nameof(RecurringShiftOccurrence),
                    occurrence.Id,
                    Detail(new
                    {
                        occurrence.SeriesId,
                        occurrence.RevisionId,
                        occurrence.LocalDate,
                        shift.Id,
                        resolution.StartsAtUtc,
                        resolution.SelectedOffset
                    }),
                    shiftId: shift.Id));
                return true;
            },
            cancellationToken);
    }

    public async Task SkipOccurrenceGapAsync(
        Guid occurrenceId,
        uint expectedVersion,
        string reason,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _recurringStore.LockRecurringOccurrenceAsync(occurrenceId, token);
                var occurrence = await RequireOccurrenceAsync(occurrenceId, token);
                if (occurrence.Version != expectedVersion)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                occurrence.Skip(reason, actor, now);
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringOccurrenceSkipped",
                    nameof(RecurringShiftOccurrence),
                    occurrence.Id,
                    Detail(new { occurrence.SeriesId, occurrence.LocalDate, Reason = reason.Trim() })));
                return true;
            },
            cancellationToken);
    }

    public async Task<RecurringRevisionPreviewDto> PreviewRevisionAsync(
        Guid seriesId,
        RecurringRevisionInput input,
        CancellationToken cancellationToken)
    {
        var series = await RequireSeriesAsync(seriesId, cancellationToken);
        if (series.Version != input.ExpectedSeriesVersion)
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var settings = await RequireSettingsAsync(cancellationToken);
        if (input.ExpectedSettingsVersion != 0 &&
            settings.Version != input.ExpectedSettingsVersion)
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        if (!string.Equals(input.TimeZoneId, settings.TimeZoneId, StringComparison.Ordinal))
        {
            throw new DomainException("The group time zone changed. Review the series zone before applying a revision.");
        }

        var current = await RequireCurrentRevisionAsync(series, cancellationToken);
        var rows = await ClassifyOccurrencesAsync(seriesId, input.EffectiveLocalDate, cancellationToken);
        ValidateEffectiveRevisionBoundary(current, settings, input.EffectiveLocalDate, rows);
        return BuildRevisionPreview(
            seriesId,
            input.EffectiveLocalDate,
            rows,
            series.Version,
            false,
            current.SignupPolicy,
            input.SignupPolicy);
    }

    public async Task<RecurringRevisionPreviewDto> PreviewZoneAdoptionAsync(
        Guid seriesId,
        uint expectedSeriesVersion,
        CancellationToken cancellationToken)
    {
        var series = await RequireSeriesAsync(seriesId, cancellationToken);
        if (series.Version != expectedSeriesVersion)
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var settings = await RequireSettingsAsync(cancellationToken);
        var current = await RequireCurrentRevisionAsync(series, cancellationToken);
        if (string.Equals(current.TimeZoneId, settings.TimeZoneId, StringComparison.Ordinal))
        {
            throw new DomainException("This recurring series already uses the group time zone.");
        }

        var candidateRows = await ClassifyOccurrencesAsync(
            seriesId,
            LocalToday(settings.TimeZoneId, _clock.UtcNow),
            cancellationToken);
        var effectiveLocalDate = FindEligibleRevisionBoundary(current, settings.TimeZoneId, candidateRows);
        var rows = await ClassifyOccurrencesAsync(seriesId, effectiveLocalDate, cancellationToken);
        ValidateEffectiveRevisionBoundary(current, settings, effectiveLocalDate, rows);
        return BuildRevisionPreview(
            seriesId,
            effectiveLocalDate,
            rows,
            series.Version,
            true,
            current.SignupPolicy,
            current.SignupPolicy);
    }

    public async Task ApplyRevisionAsync(
        Guid seriesId,
        RecurringRevisionInput input,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _workflowStore.LockGroupSettingsAsync(token);
                var settings = await RequireSettingsAsync(token);
                if (input.ExpectedSettingsVersion != 0 &&
                    settings.Version != input.ExpectedSettingsVersion)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                if (!string.Equals(input.TimeZoneId, settings.TimeZoneId, StringComparison.Ordinal))
                {
                    throw new DomainException("The group time zone changed. Review the series zone before applying a revision.");
                }

                await _recurringStore.LockRecurringSeriesAsync(seriesId, token);
                var series = await RequireSeriesAsync(seriesId, token);
                if (series.Version != input.ExpectedSeriesVersion)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                var current = await RequireCurrentRevisionAsync(series, token);
                if (input.ExpectedCurrentPolicy.HasValue &&
                    input.ExpectedCurrentPolicy.Value != current.SignupPolicy)
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                if (input.SignupPolicy != current.SignupPolicy &&
                    (!input.ConfirmPolicyChange ||
                     input.ExpectedClassification is null))
                {
                    throw new DomainException(
                        PolicyConsequence(input.SignupPolicy));
                }

                await LockConcreteTargetsAsync(seriesId, input.EffectiveLocalDate, null, token);
                var rows = await ClassifyOccurrencesAsync(seriesId, input.EffectiveLocalDate, token);
                var expected = BuildClassification(rows);
                if (input.ExpectedClassification is not null &&
                    !string.Equals(input.ExpectedClassification, expected, StringComparison.Ordinal))
                {
                    throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
                }

                ValidateEffectiveRevisionBoundary(current, settings, input.EffectiveLocalDate, rows);

                var revision = BuildRevision(
                    series.Id,
                    series.CurrentRevisionNumber + 1,
                    input,
                    input.EffectiveLocalDate,
                    input.TimeZoneId,
                    actor,
                    now);
                series.AddRevision(revision, now);
                _recurringStore.AddRecurringRevision(revision);

                foreach (var row in rows)
                {
                    if (row.Classification == "Protected")
                    {
                        var protectedOccurrence = await RequireOccurrenceAsync(row.OccurrenceId, token);
                        protectedOccurrence.MarkException(ProtectedExceptionReason);
                        continue;
                    }

                    if (row.Classification != "Eligible")
                    {
                        continue;
                    }

                    var occurrence = await RequireOccurrenceAsync(row.OccurrenceId, token);
                    if (!occurrence.ShiftId.HasValue)
                    {
                        continue;
                    }

                    var shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, token)
                        ?? throw new DomainException("A generated occurrence is missing its concrete shift.");
                    if (!revision.IncludesDate(occurrence.LocalDate))
                    {
                        occurrence.MarkException("The prior concrete occurrence was retained after the recurrence rule changed.");
                        continue;
                    }

                    var resolution = RecurringLocalTimeResolver.Resolve(
                        revision.TimeZoneId,
                        occurrence.LocalDate,
                        revision.LocalStartTime,
                        revision.AmbiguousTimeChoice);
                    if (!resolution.IsResolved)
                    {
                        occurrence.MarkException("The revised local start requires coordinator review; the prior shift was retained.");
                        continue;
                    }

                    shift.Edit(
                        revision.Title,
                        revision.Location,
                        revision.InternalCoordinatorNotes,
                        revision.VolunteerInstructions,
                        resolution.StartsAtUtc!.Value,
                        resolution.StartsAtUtc.Value.AddMinutes(revision.DurationMinutes),
                        now);
                    shift.ChangeSignupPolicy(revision.SignupPolicy);
                    var existingSlotIds = shift.Slots.Select(x => x.Id).ToHashSet();
                    shift.ConfigureBackupSlots(revision.BackupSlotCount);
                    _workflowStore.AddShiftSlots(shift.Slots.Where(x => !existingSlotIds.Contains(x.Id)).ToArray());
                    occurrence.ApplyRevision(revision.Id);
                }

                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringSeriesRevisionCreated",
                    nameof(RecurringShiftSeriesRevision),
                    revision.Id,
                    Detail(new
                    {
                        series.Id,
                        revision.RevisionNumber,
                        revision.EffectiveLocalDate,
                        Protected = rows.Count(x => x.Classification == "Protected"),
                        Eligible = rows.Count(x => x.Classification == "Eligible"),
                        revision.SignupPolicy
                    })));
                return true;
            },
            cancellationToken);
    }

    public Task AdoptGroupZoneAsync(
        Guid seriesId,
        uint expectedSeriesVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        return AdoptGroupZoneCoreAsync(seriesId, expectedSeriesVersion, coordinatorEmail, cancellationToken);
    }

    private async Task AdoptGroupZoneCoreAsync(
        Guid seriesId,
        uint expectedSeriesVersion,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var settings = await RequireSettingsAsync(cancellationToken);
        var series = await RequireSeriesAsync(seriesId, cancellationToken);
        if (series.Version != expectedSeriesVersion)
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var current = await RequireCurrentRevisionAsync(series, cancellationToken);
        var candidateRows = await ClassifyOccurrencesAsync(
            seriesId,
            LocalToday(settings.TimeZoneId, _clock.UtcNow),
            cancellationToken);
        var effectiveLocalDate = FindEligibleRevisionBoundary(current, settings.TimeZoneId, candidateRows);
        var input = new RecurringRevisionInput(
            effectiveLocalDate,
            current.Title,
            current.Location,
            current.VolunteerInstructions,
            current.InternalCoordinatorNotes,
            current.RecurrenceKind,
            current.Interval,
            RecurringCalendar.ToWeekdays(current.WeeklyDays),
            current.AnchorLocalDate,
            current.LocalStartTime,
            current.DurationMinutes,
            current.BackupSlotCount,
            current.HorizonWeeks,
            settings.TimeZoneId,
            current.AmbiguousTimeChoice,
            expectedSeriesVersion,
            null,
            settings.Version,
            current.SignupPolicy);
        await ApplyRevisionAsync(seriesId, input, coordinatorEmail, cancellationToken);
    }
    public async Task<RecurringPublicationPreviewDto> PreviewPublicationAsync(
        Guid seriesId,
        DateOnly fromLocalDate,
        DateOnly throughLocalDate,
        CancellationToken cancellationToken)
    {
        if (throughLocalDate < fromLocalDate)
        {
            throw new DomainException("The publication end date must be on or after the start date.");
        }

        var series = await RequireSeriesAsync(seriesId, cancellationToken);
        var settings = await RequireSettingsAsync(cancellationToken);
        var revisions = await _recurringStore.GetRecurringRevisionsAsync(seriesId, cancellationToken);
        var occurrences = await _recurringStore.GetRecurringOccurrencesAsync(
            seriesId,
            fromLocalDate,
            throughLocalDate,
            cancellationToken);
        var previews = await BuildOccurrencePreviewsAsync(occurrences, revisions, settings.TimeZoneId, cancellationToken);
        var blockers = await BuildPublicationBlockersAsync(
            series,
            revisions,
            occurrences,
            settings,
            null,
            cancellationToken);
        return new RecurringPublicationPreviewDto(
            seriesId,
            fromLocalDate,
            throughLocalDate,
            previews,
            blockers,
            await BuildOccurrenceVersionsAsync(occurrences, cancellationToken),
            series.Version);
    }

    public async Task<RecurringCommandResult> PublishRecurringOccurrencesAsync(
        Guid seriesId,
        DateOnly fromLocalDate,
        DateOnly throughLocalDate,
        uint expectedSeriesVersion,
        string expectedVersions,
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        return await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _workflowStore.LockGroupSettingsAsync(token);
                await _recurringStore.LockRecurringSeriesAsync(seriesId, token);
                var series = await RequireSeriesAsync(seriesId, token);
                if (series.Version != expectedSeriesVersion)
                {
                    throw new DomainException("Publication changed while this review was open. Reload the range.");
                }

                await LockConcreteTargetsAsync(
                    seriesId,
                    fromLocalDate,
                    throughLocalDate,
                    token);
                var occurrences = await _recurringStore.GetRecurringOccurrencesAsync(
                    seriesId,
                    fromLocalDate,
                    throughLocalDate,
                    token);
                var settings = await RequireSettingsAsync(token);
                var revisions = await _recurringStore.GetRecurringRevisionsAsync(seriesId, token);
                var blockers = await BuildPublicationBlockersAsync(
                    series,
                    revisions,
                    occurrences,
                    settings,
                    expectedVersions,
                    token);
                if (blockers.Count > 0)
                {
                    throw new DomainException(BuildBlockerMessage(blockers));
                }

                var shifts = new List<Shift>();
                foreach (var occurrence in occurrences.OrderBy(x => x.ShiftId))
                {
                    var shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId!.Value, token)
                        ?? throw new DomainException(BuildBlockerMessage(
                            [new RecurringPublicationBlockerDto(occurrence.LocalDate, "The concrete shift is missing.")]));
                    shifts.Add(shift);
                }

                foreach (var shift in shifts.OrderBy(x => x.Id))
                {
                    shift.Publish(now);
                    _workflowStore.AddAuditEntry(AuditEntry.Create(
                        now,
                        actor,
                        "ShiftPublished",
                        nameof(Shift),
                        shift.Id,
                        Detail(new { shift.PublishedAtUtc, Recurring = true }),
                        shiftId: shift.Id));
                }

                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringOccurrencesPublished",
                    nameof(RecurringShiftSeries),
                    series.Id,
                    Detail(new
                    {
                        FromLocalDate = fromLocalDate,
                        ThroughLocalDate = throughLocalDate,
                        Count = shifts.Count
                    }),
                    shiftId: shifts.Select(x => (Guid?)x.Id).FirstOrDefault()));
                return new RecurringCommandResult(shifts.Count, []);
            },
            cancellationToken);
    }

    public async Task MarkOccurrenceExceptionAsync(
        Guid occurrenceId,
        string coordinatorEmail,
        string? reason,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _workflowStore.ExecuteInTransactionAsync(
            async token =>
            {
                await _recurringStore.LockRecurringOccurrenceAsync(occurrenceId, token);
                var occurrence = await RequireOccurrenceAsync(occurrenceId, token);
                occurrence.MarkException(reason);
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringOccurrenceDetached",
                    nameof(RecurringShiftOccurrence),
                    occurrence.Id,
                    Detail(new { occurrence.SeriesId, occurrence.LocalDate, Reason = reason }),
                    shiftId: occurrence.ShiftId));
                return true;
            },
            cancellationToken);
    }

    private async Task LockConcreteTargetsAsync(
        Guid seriesId,
        DateOnly fromLocalDate,
        DateOnly? throughLocalDate,
        CancellationToken cancellationToken)
    {
        var initialOccurrences = await _recurringStore.GetRecurringOccurrencesAsync(
            seriesId,
            fromLocalDate,
            throughLocalDate,
            cancellationToken);
        var initialOccurrenceIds = initialOccurrences.Select(x => x.Id).ToHashSet();
        var initialShifts = new Dictionary<Guid, Shift>();
        foreach (var shiftId in initialOccurrences
                     .Where(x => x.ShiftId.HasValue)
                     .Select(x => x.ShiftId!.Value)
                     .Distinct()
                     .OrderBy(x => x))
        {
            if (await _workflowStore.GetShiftAsync(shiftId, cancellationToken) is { } shift)
            {
                initialShifts[shift.Id] = shift;
            }
        }

        var initialShiftIds = initialShifts.Keys.ToHashSet();
        var initialSlotIds = initialShifts.Values
            .SelectMany(x => x.Slots)
            .Select(x => x.Id)
            .ToHashSet();
        foreach (var slotId in initialSlotIds.OrderBy(x => x))
        {
            await _workflowStore.LockSlotAsync(slotId, cancellationToken);
        }

        foreach (var shiftId in initialShiftIds.OrderBy(x => x))
        {
            await _workflowStore.LockShiftAsync(shiftId, cancellationToken);
        }

        var lockedSlotIds = new HashSet<Guid>();
        foreach (var shiftId in initialShiftIds.OrderBy(x => x))
        {
            var shift = await _workflowStore.GetShiftAsync(shiftId, cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            foreach (var slot in shift.Slots)
            {
                lockedSlotIds.Add(slot.Id);
            }
        }

        if (!initialSlotIds.SetEquals(lockedSlotIds))
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        foreach (var occurrenceId in initialOccurrenceIds.OrderBy(x => x))
        {
            await _recurringStore.LockRecurringOccurrenceAsync(occurrenceId, cancellationToken);
        }

        var finalOccurrences = await _recurringStore.GetRecurringOccurrencesAsync(
            seriesId,
            fromLocalDate,
            throughLocalDate,
            cancellationToken);
        if (!initialOccurrenceIds.SetEquals(finalOccurrences.Select(x => x.Id)) ||
            !initialShiftIds.SetEquals(
                finalOccurrences
                    .Where(x => x.ShiftId.HasValue)
                    .Select(x => x.ShiftId!.Value)))
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }

        var finalSlotIds = new HashSet<Guid>();
        foreach (var shiftId in initialShiftIds.OrderBy(x => x))
        {
            var shift = await _workflowStore.GetShiftAsync(shiftId, cancellationToken)
                ?? throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
            foreach (var slot in shift.Slots)
            {
                finalSlotIds.Add(slot.Id);
            }
        }

        if (!initialSlotIds.SetEquals(finalSlotIds))
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }
    }


    private async Task GenerateForRangeAsync(
        RecurringShiftSeries series,
        IReadOnlyList<RecurringShiftSeriesRevision> revisions,
        DateOnly fromLocalDate,
        DateOnly throughLocalDate,
        string actor,
        CancellationToken cancellationToken)
    {
        if (throughLocalDate < fromLocalDate)
        {
            return;
        }

        var existing = (await _recurringStore.GetRecurringOccurrencesAsync(
            series.Id,
            fromLocalDate,
            throughLocalDate,
            cancellationToken)).ToDictionary(x => x.LocalDate);
        var now = _clock.UtcNow;
        for (var date = fromLocalDate; date <= throughLocalDate; date = date.AddDays(1))
        {
            var revision = revisions
                .Where(x => x.EffectiveLocalDate <= date)
                .OrderByDescending(x => x.EffectiveLocalDate)
                .ThenByDescending(x => x.RevisionNumber)
                .FirstOrDefault();
            if (revision is null || !revision.IncludesDate(date) || existing.ContainsKey(date))
            {
                continue;
            }

            var resolution = RecurringLocalTimeResolver.Resolve(
                revision.TimeZoneId,
                date,
                revision.LocalStartTime,
                revision.AmbiguousTimeChoice);
            if (!resolution.IsResolved)
            {
                var gap = RecurringShiftOccurrence.CreateNeedsReview(series.Id, revision.Id, date, now);
                _recurringStore.AddRecurringOccurrence(gap);
                existing[date] = gap;
                _workflowStore.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "RecurringOccurrenceNeedsReview",
                    nameof(RecurringShiftOccurrence),
                    gap.Id,
                    Detail(new
                    {
                        SeriesId = series.Id,
                        RevisionId = revision.Id,
                        LocalDate = date,
                        revision.TimeZoneId,
                        LocalStart = revision.LocalStartTime
                    })));
                continue;
            }

            var pending = RecurringShiftOccurrence.CreatePendingGenerated(series.Id, revision.Id, date, now);
            var shift = CreateShiftFromRevision(revision, resolution.StartsAtUtc!.Value, pending.Id);
            pending.AttachShift(shift.Id);
            _workflowStore.AddShift(shift);
            _recurringStore.AddRecurringOccurrence(pending);
            existing[date] = pending;
            _workflowStore.AddAuditEntry(AuditEntry.Create(
                now,
                actor,
                "RecurringOccurrenceGenerated",
                nameof(RecurringShiftOccurrence),
                pending.Id,
                Detail(new
                {
                    SeriesId = series.Id,
                    RevisionId = revision.Id,
                    OccurrenceId = pending.Id,
                    ShiftId = shift.Id,
                    LocalDate = date,
                    revision.TimeZoneId,
                    resolution.StartsAtUtc,
                    EndsAtUtc = resolution.StartsAtUtc.Value.AddMinutes(revision.DurationMinutes),
                    resolution.SelectedOffset,
                    revision.AmbiguousTimeChoice
                }),
                shiftId: shift.Id));
        }
    }

    private async Task<IReadOnlyList<ClassificationRow>> ClassifyOccurrencesAsync(
        Guid seriesId,
        DateOnly effectiveLocalDate,
        CancellationToken cancellationToken)
    {
        var occurrences = await _recurringStore.GetRecurringOccurrencesAsync(
            seriesId,
            effectiveLocalDate,
            null,
            cancellationToken);
        var rows = new List<ClassificationRow>(occurrences.Count);
        foreach (var occurrence in occurrences)
        {
            Shift? shift = null;
            if (occurrence.ShiftId.HasValue)
            {
                shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, cancellationToken);
            }

            var shiftVersion = shift?.Version ?? 0;
            if (occurrence.Status == RecurringOccurrenceStatus.NeedsReview)
            {
                rows.Add(new ClassificationRow(
                    occurrence.Id,
                    occurrence.LocalDate,
                    "NeedsReview",
                    null,
                    occurrence.Version,
                    shiftVersion));
                continue;
            }

            if (occurrence.Status == RecurringOccurrenceStatus.Skipped)
            {
                rows.Add(new ClassificationRow(
                    occurrence.Id,
                    occurrence.LocalDate,
                    "Skipped",
                    null,
                    occurrence.Version,
                    shiftVersion));
                continue;
            }

            if (occurrence.IsException)
            {
                rows.Add(new ClassificationRow(
                    occurrence.Id,
                    occurrence.LocalDate,
                    "Exception",
                    occurrence.ResolutionReason,
                    occurrence.Version,
                    shiftVersion));
                continue;
            }

            if (shift is null)
            {
                rows.Add(new ClassificationRow(
                    occurrence.Id,
                    occurrence.LocalDate,
                    "NeedsReview",
                    "Concrete shift is missing.",
                    occurrence.Version,
                    0));
                continue;
            }

            var slotIds = shift.Slots.Where(x => x.IsActive).Select(x => x.Id).ToArray();
            var pending = await _workflowStore.GetPendingRequestsAsync(slotIds, cancellationToken);
            var assignments = await _workflowStore.GetActiveAssignmentsAsync(slotIds, cancellationToken);
            if (pending.Count == 0 && assignments.Count == 0)
            {
                rows.Add(new ClassificationRow(
                    occurrence.Id,
                    occurrence.LocalDate,
                    "Eligible",
                    null,
                    occurrence.Version,
                    shiftVersion));
                continue;
            }

            var volunteerIds = pending.Select(x => x.VolunteerId)
                .Concat(assignments.Select(x => x.VolunteerId))
                .Distinct()
                .ToArray();
            var volunteers = await _workflowStore.GetVolunteersByIdsAsync(volunteerIds, cancellationToken);
            var names = volunteers
                .Select(x => x.AnonymizedAtUtc.HasValue ? "Removed volunteer" : x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            rows.Add(new ClassificationRow(
                occurrence.Id,
                occurrence.LocalDate,
                "Protected",
                names.Length == 0 ? "Active request or assignment" : string.Join(", ", names),
                occurrence.Version,
                shiftVersion));
        }

        return rows.OrderBy(x => x.LocalDate).ThenBy(x => x.OccurrenceId).ToArray();
    }

    private async Task<IReadOnlyList<RecurringPublicationBlockerDto>> BuildPublicationBlockersAsync(
        RecurringShiftSeries series,
        IReadOnlyList<RecurringShiftSeriesRevision> revisions,
        IReadOnlyList<RecurringShiftOccurrence> occurrences,
        GroupSettings settings,
        string? expectedVersions,
        CancellationToken cancellationToken)
    {
        var blockers = new List<RecurringPublicationBlockerDto>();
        var expected = ParseOccurrenceVersions(expectedVersions);
        var currentRevision = revisions.LastOrDefault(x => x.RevisionNumber == series.CurrentRevisionNumber);
        var currentDates = occurrences.Select(x => x.LocalDate).ToHashSet();
        if (expectedVersions is not null)
        {
            foreach (var missingDate in expected.Keys.Where(x => !currentDates.Contains(x)).OrderBy(x => x))
            {
                blockers.Add(new RecurringPublicationBlockerDto(
                    missingDate,
                    "The selected occurrence changed; reload the review."));
            }
        }

        foreach (var occurrence in occurrences.OrderBy(x => x.LocalDate))
        {
            if (expectedVersions is not null &&
                (!expected.TryGetValue(occurrence.LocalDate, out var expectedVersionsForDate) ||
                 expectedVersionsForDate.OccurrenceVersion != occurrence.Version))
            {
                blockers.Add(new RecurringPublicationBlockerDto(
                    occurrence.LocalDate,
                    "This occurrence changed; reload the review."));
            }

            if (occurrence.Status == RecurringOccurrenceStatus.NeedsReview)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "The local start needs review."));
                continue;
            }

            if (occurrence.Status == RecurringOccurrenceStatus.Skipped)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "This occurrence was explicitly skipped."));
                continue;
            }

            if (occurrence.IsException)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "This occurrence is a protected or manual exception."));
            }

            if (!occurrence.ShiftId.HasValue)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "The concrete shift is missing."));
                continue;
            }

            var shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, cancellationToken);
            if (shift is null)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "The concrete shift is missing."));
                continue;
            }
            if (expectedVersions is not null &&
                (!expected.TryGetValue(occurrence.LocalDate, out expectedVersionsForDate) ||
                 expectedVersionsForDate.ShiftVersion != shift.Version))
            {
                blockers.Add(new RecurringPublicationBlockerDto(
                    occurrence.LocalDate,
                    "The concrete shift changed; reload the review."));
            }

            var slotIds = shift.Slots.Select(x => x.Id).ToArray();
            var pendingRequests = await _workflowStore.GetPendingRequestsAsync(slotIds, cancellationToken);
            var activeAssignments = await _workflowStore.GetActiveAssignmentsAsync(slotIds, cancellationToken);
            if (pendingRequests.Count > 0 || activeAssignments.Count > 0)
            {
                blockers.Add(new RecurringPublicationBlockerDto(
                    occurrence.LocalDate,
                    "This occurrence has a pending request or active assignment."));
            }

            if (shift.PublishedAtUtc.HasValue)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "The shift is already published."));
            }
            else if (!shift.IsActive)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "The shift is inactive."));
            }
            else if (shift.EndsAtUtc <= _clock.UtcNow)
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "The shift has ended."));
            }

            if (currentRevision is null ||
                !string.Equals(currentRevision.TimeZoneId, settings.TimeZoneId, StringComparison.Ordinal))
            {
                blockers.Add(new RecurringPublicationBlockerDto(occurrence.LocalDate, "The series needs group time-zone review."));
            }
        }

        return blockers
            .GroupBy(x => (x.LocalDate, x.Reason))
            .Select(x => x.First())
            .OrderBy(x => x.LocalDate)
            .ThenBy(x => x.Reason, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<RecurringOccurrencePreviewDto>> BuildOccurrencePreviewsAsync(
        IReadOnlyList<RecurringShiftOccurrence> occurrences,
        IReadOnlyList<RecurringShiftSeriesRevision> revisions,
        string displayTimeZoneId,
        CancellationToken cancellationToken)
    {
        var result = new List<RecurringOccurrencePreviewDto>(occurrences.Count);
        foreach (var occurrence in occurrences)
        {
            var revision = revisions.FirstOrDefault(x => x.Id == occurrence.RevisionId)
                ?? throw new DomainException("A recurring occurrence revision is missing.");
            Shift? shift = null;
            if (occurrence.ShiftId.HasValue)
            {
                shift = await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, cancellationToken);
            }

            result.Add(BuildOccurrencePreview(occurrence, revision, shift, displayTimeZoneId));
        }

        return result;
    }

    private static RecurringOccurrencePreviewDto BuildOccurrencePreview(
        RecurringShiftOccurrence occurrence,
        RecurringShiftSeriesRevision revision,
        Shift? shift,
        string displayTimeZoneId)
    {
        var localStart = occurrence.ResolvedLocalStart ??
            DateTime.SpecifyKind(occurrence.LocalDate.ToDateTime(revision.LocalStartTime), DateTimeKind.Unspecified);
        if (shift is not null)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(displayTimeZoneId);
            var local = TimeZoneInfo.ConvertTime(shift.StartsAtUtc, zone);
            return new RecurringOccurrencePreviewDto(
                occurrence.Id,
                occurrence.LocalDate,
                local.DateTime,
                TimeZoneInfo.ConvertTime(shift.EndsAtUtc, zone).DateTime,
                shift.StartsAtUtc,
                shift.EndsAtUtc,
                occurrence.Status.ToString(),
                occurrence.ResolvedLocalStart.HasValue ? "Resolved exception" : DstStatus(revision, shift.StartsAtUtc),
                TimeZoneLabels.OffsetValue(local.Offset),
                shift.PublishedAtUtc.HasValue,
                occurrence.IsException,
                occurrence.ResolutionReason)
            {
                RoleLabels = shift.Slots
                    .Where(x => x.IsActive)
                    .OrderBy(SlotOrder)
                    .Select(SlotLabel)
                    .ToArray()
            };
        }

        var status = occurrence.Status == RecurringOccurrenceStatus.NeedsReview
            ? "Clock gap: this local start does not exist"
            : occurrence.Status.ToString();
        return new RecurringOccurrencePreviewDto(
            occurrence.Id,
            occurrence.LocalDate,
            localStart,
            localStart.AddMinutes(revision.DurationMinutes),
            null,
            null,
            occurrence.Status.ToString(),
            status,
            null,
            false,
            occurrence.IsException,
            occurrence.ResolutionReason)
        {
            RoleLabels = RoleLabels(revision.BackupSlotCount)
        };
    }

    private DateOnly FindEligibleRevisionBoundary(
        RecurringShiftSeriesRevision currentRevision,
        string todayTimeZoneId,
        IReadOnlyList<ClassificationRow> rows)
    {
        var today = LocalToday(todayTimeZoneId, _clock.UtcNow);
        var through = today.AddDays(currentRevision.HorizonWeeks * 7);
        var boundary = rows
            .Where(x =>
                x.LocalDate > currentRevision.EffectiveLocalDate &&
                x.LocalDate >= today &&
                x.LocalDate <= through &&
                x.Classification == "Eligible")
            .OrderBy(x => x.LocalDate)
            .Select(x => (DateOnly?)x.LocalDate)
            .FirstOrDefault();
        return boundary
            ?? throw new DomainException("Choose an existing eligible generated occurrence within the current horizon.");
    }

    private void ValidateEffectiveRevisionBoundary(
        RecurringShiftSeriesRevision currentRevision,
        GroupSettings settings,
        DateOnly effectiveLocalDate,
        IReadOnlyList<ClassificationRow> rows)
    {
        if (effectiveLocalDate <= currentRevision.EffectiveLocalDate)
        {
            throw new DomainException("A new revision must start at a later occurrence boundary.");
        }

        var today = LocalToday(settings.TimeZoneId, _clock.UtcNow);
        var through = today.AddDays(currentRevision.HorizonWeeks * 7);
        if (effectiveLocalDate < today || effectiveLocalDate > through)
        {
            throw new DomainException("Choose an occurrence boundary within the current future horizon.");
        }

        var boundary = rows.SingleOrDefault(x => x.LocalDate == effectiveLocalDate);
        if (boundary is null ||
            boundary.Classification != "Eligible")
        {
            throw new DomainException("Choose an existing eligible generated occurrence within the current horizon.");
        }
    }

    private RecurringSeriesPreviewDto BuildPreview(
        RecurringShiftSeriesRevision revision,
        string displayTimeZoneId,
        uint expectedSettingsVersion,
        IReadOnlyDictionary<DateOnly, RecurringShiftOccurrence>? existingOccurrences,
        int horizonWeeks)
    {
        var through = revision.AnchorLocalDate.AddDays(horizonWeeks * 7);
        var rows = RecurringCalendar.EnumerateDates(revision, revision.AnchorLocalDate, through)
            .Select(date =>
            {
                var resolution = RecurringLocalTimeResolver.Resolve(
                    revision.TimeZoneId,
                    date,
                    revision.LocalStartTime,
                    revision.AmbiguousTimeChoice);
                var localStart = DateTime.SpecifyKind(date.ToDateTime(revision.LocalStartTime), DateTimeKind.Unspecified);
                var existing = existingOccurrences is not null && existingOccurrences.TryGetValue(date, out var occurrence)
                    ? occurrence
                    : null;
                if (!resolution.IsResolved)
                {
                    return new RecurringOccurrencePreviewDto(
                        existing?.Id,
                        date,
                        localStart,
                        localStart.AddMinutes(revision.DurationMinutes),
                        null,
                        null,
                        nameof(RecurringOccurrenceStatus.NeedsReview),
                        "Clock gap: this local start does not exist",
                        null,
                        false,
                        existing?.IsException ?? false)
                    {
                        RoleLabels = RoleLabels(revision.BackupSlotCount)
                    };
                }

                var end = resolution.StartsAtUtc!.Value.AddMinutes(revision.DurationMinutes);
                var zone = TimeZoneInfo.FindSystemTimeZoneById(displayTimeZoneId);
                var displayedStart = TimeZoneInfo.ConvertTime(resolution.StartsAtUtc.Value, zone);
                return new RecurringOccurrencePreviewDto(
                    existing?.Id,
                    date,
                    displayedStart.DateTime,
                    TimeZoneInfo.ConvertTime(end, zone).DateTime,
                    resolution.StartsAtUtc,
                    end,
                    nameof(RecurringOccurrenceStatus.Generated),
                    resolution.Candidates.Count > 0
                        ? revision.AmbiguousTimeChoice == AmbiguousTimeChoice.FirstOccurrence
                            ? "Overlap: first occurrence"
                            : "Overlap: second occurrence"
                        : "Ordinary local start",
                    TimeZoneLabels.OffsetValue(displayedStart.Offset),
                    false,
                    existing?.IsException ?? false)
                {
                    RoleLabels = RoleLabels(revision.BackupSlotCount)
                };
            })
            .ToArray();
        return new RecurringSeriesPreviewDto(
            revision.TimeZoneId,
            revision.RecurrenceKind,
            revision.Interval,
            RecurringCalendar.ToWeekdays(revision.WeeklyDays),
            revision.AnchorLocalDate,
            revision.LocalStartTime,
            revision.DurationMinutes,
            revision.HorizonWeeks,
            revision.AmbiguousTimeChoice,
            rows,
            rows.Count(x => x.Status == nameof(RecurringOccurrenceStatus.NeedsReview)),
            0,
            expectedSettingsVersion,
            BuildClassification(rows),
            false,
            revision.SignupPolicy);
    }

    private static RecurringRevisionPreviewDto BuildRevisionPreview(
        Guid seriesId,
        DateOnly effectiveLocalDate,
        IReadOnlyList<ClassificationRow> rows,
        uint expectedSeriesVersion,
        bool isZoneAdoption,
        SignupPolicy currentPolicy,
        SignupPolicy proposedPolicy)
    {
        return new RecurringRevisionPreviewDto(
            seriesId,
            effectiveLocalDate,
            rows.Select(row => new RecurringRevisionRowDto(
                row.OccurrenceId,
                row.LocalDate,
                row.Classification,
                row.ProtectionSummary,
                row.OccurrenceVersion)).ToArray(),
            rows.Count(x => x.Classification == "Eligible"),
            rows.Count(x => x.Classification == "Protected"),
            rows.Count(x => x.Classification == "Exception"),
            rows.Count(x => x.Classification == "NeedsReview"),
            rows.Count(x => x.Classification == "Skipped"),
            expectedSeriesVersion,
            BuildClassification(rows),
            isZoneAdoption,
            currentPolicy,
            proposedPolicy,
            PolicyConsequence(proposedPolicy));
    }

    private static string BuildClassification(IEnumerable<ClassificationRow> rows) =>
        string.Join(";", rows
            .OrderBy(x => x.LocalDate)
            .ThenBy(x => x.OccurrenceId)
            .Select(x => $"{x.OccurrenceId:N}:{x.LocalDate:yyyy-MM-dd}:{x.Classification}:{x.OccurrenceVersion}:{x.ShiftVersion}"));

    private static string BuildClassification(IEnumerable<RecurringOccurrencePreviewDto> rows) =>
        string.Join(";", rows.Select(x => $"{x.LocalDate:yyyy-MM-dd}:{x.Status}:{x.DstStatus}"));

    private async Task<string> BuildOccurrenceVersionsAsync(
        IEnumerable<RecurringShiftOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        var entries = new List<string>();
        foreach (var occurrence in occurrences.OrderBy(x => x.LocalDate))
        {
            var shiftVersion = 0u;
            if (occurrence.ShiftId.HasValue &&
                await _workflowStore.GetShiftAsync(occurrence.ShiftId.Value, cancellationToken) is { } shift)
            {
                shiftVersion = shift.Version;
            }

            entries.Add($"{occurrence.LocalDate:yyyy-MM-dd}:{occurrence.Version}:{shiftVersion}");
        }

        return string.Join(";", entries);
    }

    private static Dictionary<DateOnly, (uint OccurrenceVersion, uint ShiftVersion)> ParseOccurrenceVersions(string? encoded)
    {
        var result = new Dictionary<DateOnly, (uint OccurrenceVersion, uint ShiftVersion)>();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return result;
        }

        foreach (var entry in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':');
            if (parts.Length == 3 &&
                DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var occurrenceVersion) &&
                uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var shiftVersion))
            {
                result[date] = (occurrenceVersion, shiftVersion);
            }
        }

        return result;
    }

    private static string DstStatus(RecurringShiftSeriesRevision revision, DateTimeOffset startsAtUtc)
    {
        if (!TimeZoneLabels.TryGetIanaZone(revision.TimeZoneId, out _, out var zone))
        {
            return "Time zone review required";
        }

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAtUtc, zone).DateTime);
        var resolution = RecurringLocalTimeResolver.Resolve(
            revision.TimeZoneId,
            localDate,
            revision.LocalStartTime,
            revision.AmbiguousTimeChoice);
        return resolution.Candidates.Count > 0
            ? revision.AmbiguousTimeChoice == AmbiguousTimeChoice.FirstOccurrence
                ? "Overlap: first occurrence"
                : "Overlap: second occurrence"
            : "Ordinary local start";
    }

    private static Shift CreateShiftFromRevision(
        RecurringShiftSeriesRevision revision,
        DateTimeOffset startsAtUtc,
        Guid occurrenceId)
    {
        return Shift.CreateRecurring(
            revision.Title,
            revision.Location,
            revision.InternalCoordinatorNotes,
            revision.VolunteerInstructions,
            startsAtUtc,
            startsAtUtc.AddMinutes(revision.DurationMinutes),
            revision.BackupSlotCount,
            occurrenceId,
            revision.SignupPolicy);
    }

    private static RecurringShiftSeriesRevision BuildRevision(
        Guid seriesId,
        int revisionNumber,
        RecurringSeriesInput input,
        DateOnly effectiveLocalDate,
        string timeZoneId,
        string actor,
        DateTimeOffset nowUtc) =>
        RecurringShiftSeriesRevision.Create(
            seriesId,
            revisionNumber,
            effectiveLocalDate,
            input.Title,
            input.Location,
            input.VolunteerInstructions,
            input.InternalCoordinatorNotes,
            input.RecurrenceKind,
            input.Interval,
            RecurringCalendar.ToMask(input.Weekdays),
            input.AnchorLocalDate,
            input.LocalStartTime,
            input.DurationMinutes,
            input.BackupSlotCount,
            input.HorizonWeeks,
            timeZoneId,
            input.AmbiguousTimeChoice,
            actor,
            nowUtc,
            input.SignupPolicy);

    private static RecurringShiftSeriesRevision BuildRevision(
        Guid seriesId,
        int revisionNumber,
        RecurringRevisionInput input,
        DateOnly effectiveLocalDate,
        string timeZoneId,
        string actor,
        DateTimeOffset nowUtc) =>
        RecurringShiftSeriesRevision.Create(
            seriesId,
            revisionNumber,
            effectiveLocalDate,
            input.Title,
            input.Location,
            input.VolunteerInstructions,
            input.InternalCoordinatorNotes,
            input.RecurrenceKind,
            input.Interval,
            RecurringCalendar.ToMask(input.Weekdays),
            input.AnchorLocalDate,
            input.LocalStartTime,
            input.DurationMinutes,
            input.BackupSlotCount,
            input.HorizonWeeks,
            timeZoneId,
            input.AmbiguousTimeChoice,
            actor,
            nowUtc,
            input.SignupPolicy);

    private static string PolicyConsequence(SignupPolicy policy) => policy switch
    {
        SignupPolicy.DirectClaim => "Direct claim is lower maintenance: the first eligible volunteer is confirmed immediately for future submissions.",
        _ => "Approval required is the safer default: a coordinator reviews each future recurring request before assignment."
    };

    private async Task<RecurringShiftSeries> RequireSeriesAsync(Guid seriesId, CancellationToken cancellationToken) =>
        await _recurringStore.GetRecurringSeriesAsync(seriesId, cancellationToken)
        ?? throw new DomainException("The recurring series was not found.");

    private async Task<RecurringShiftSeriesRevision> RequireRevisionAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        return await _recurringStore.GetRecurringRevisionAsync(revisionId, cancellationToken)
            ?? throw new DomainException("The recurring revision was not found.");
    }

    private async Task<RecurringShiftSeriesRevision> RequireCurrentRevisionAsync(
        RecurringShiftSeries series,
        CancellationToken cancellationToken)
    {
        var revisions = await _recurringStore.GetRecurringRevisionsAsync(series.Id, cancellationToken);
        return revisions.LastOrDefault(x => x.RevisionNumber == series.CurrentRevisionNumber)
            ?? throw new DomainException("The recurring series revision was not found.");
    }

    private async Task<RecurringShiftOccurrence> RequireOccurrenceAsync(Guid occurrenceId, CancellationToken cancellationToken) =>
        await _recurringStore.GetRecurringOccurrenceAsync(occurrenceId, cancellationToken)
        ?? throw new DomainException("The recurring occurrence was not found.");

    private async Task<GroupSettings> RequireSettingsAsync(CancellationToken cancellationToken) =>
        await _workflowStore.GetGroupSettingsAsync(cancellationToken)
        ?? throw new DomainException(VolunteerCoordinatorService.CommitmentUnavailableMessage);

    private static void ValidateSettingsVersion(GroupSettings settings, uint expectedVersion)
    {
        if (settings.Version != expectedVersion)
        {
            throw new DomainException(VolunteerCoordinatorService.StalePreviewMessage);
        }
    }

    private static string RequireCoordinator(string coordinatorEmail)
    {
        if (string.IsNullOrWhiteSpace(coordinatorEmail))
        {
            throw new DomainException("A coordinator identity is required.");
        }

        return coordinatorEmail.Trim().ToUpperInvariant();
    }

    private static DateOnly LocalToday(string timeZoneId, DateTimeOffset utcNow)
    {
        if (!TimeZoneLabels.TryGetIanaZone(timeZoneId, out _, out var zone))
        {
            throw new DomainException("The group time zone is unavailable.");
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
    }

    private static string RecurrenceDescription(RecurringShiftSeriesRevision revision)
    {
        var interval = revision.RecurrenceKind == RecurrenceKind.Daily
            ? $"Every {revision.Interval} day{(revision.Interval == 1 ? string.Empty : "s")}"
            : $"Every {revision.Interval} week{(revision.Interval == 1 ? string.Empty : "s")} on " +
              string.Join(", ", RecurringCalendar.ToWeekdays(revision.WeeklyDays).Select(x => x.ToString()));
        return $"{interval} at {revision.LocalStartTime:hh\\:mm} for {revision.DurationMinutes} minutes";
    }

    private static IReadOnlyList<string> RoleLabels(int backupSlotCount) =>
        new[] { "Primary" }.Concat(Enumerable.Range(1, backupSlotCount).Select(x => $"Backup {x}")).ToArray();

    private static int SlotOrder(ShiftSlot slot) =>
        slot.Kind == SlotKind.Primary ? 0 : slot.Position;

    private static string SlotLabel(ShiftSlot slot) =>
        slot.Kind == SlotKind.Primary ? "Primary" : $"Backup {slot.Position}";

    private static string BuildBlockerMessage(IEnumerable<RecurringPublicationBlockerDto> blockers) =>
        "Publication was not applied. " + string.Join(
            " ",
            blockers
                .OrderBy(x => x.LocalDate)
                .ThenBy(x => x.Reason, StringComparer.Ordinal)
                .Select(x => $"{x.LocalDate:yyyy-MM-dd}: {x.Reason}"));

    private static string Detail(object value) => JsonSerializer.Serialize(value);

    private sealed record ClassificationRow(
        Guid OccurrenceId,
        DateOnly LocalDate,
        string Classification,
        string? ProtectionSummary,
        uint OccurrenceVersion,
        uint ShiftVersion);
}
