using System.Security.Cryptography;
using System.Text;
using VolunteerCoordinator.Application.Models;
using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Auditing;

namespace VolunteerCoordinator.Application;

public sealed partial class VolunteerCoordinatorService
{
    public async Task<CoordinatorWorkSummaryDto> GetCoordinatorWorkSummaryAsync(
        CoordinatorAttentionOptions? options,
        CancellationToken cancellationToken)
    {
        var effectiveOptions = options ?? new CoordinatorAttentionOptions();
        if (!effectiveOptions.IsValid())
        {
            throw new DomainException("Coordinator attention thresholds are invalid.");
        }

        return await _store.GetCoordinatorWorkSummaryAsync(
            _clock.UtcNow,
            effectiveOptions,
            cancellationToken);
    }

    public async Task<CoordinatorWorkPageDto> GetCoordinatorWorkPageAsync(
        CoordinatorWorkFilter filter,
        CoordinatorWorkCursor? cursor,
        CoordinatorAttentionOptions? options,
        CancellationToken cancellationToken)
    {
        var effectiveOptions = options ?? new CoordinatorAttentionOptions();
        if (!effectiveOptions.IsValid())
        {
            throw new DomainException("Coordinator attention thresholds are invalid.");
        }

        return await _store.GetCoordinatorWorkPageAsync(
            _clock.UtcNow,
            effectiveOptions,
            filter,
            cursor,
            cancellationToken);
    }
    private static void AddWorkAttention(
        ICollection<CoordinatorAttentionDto> attention,
        CoordinatorWorkSummaryDto summary,
        string category,
        string label,
        string actionLabel)
    {
        var count = summary.Count(category);
        if (count == 0)
        {
            return;
        }

        attention.Add(new CoordinatorAttentionDto(
            category,
            label,
            count,
            $"/Coordinator/Work?category={category}",
            actionLabel,
            []));
    }

    public async Task<IReadOnlyList<VolunteerSearchResultDto>> SearchAssignableVolunteersAsync(
        string? term,
        CancellationToken cancellationToken)
    {
        var normalizedTerm = term?.Trim() ?? string.Empty;
        if (normalizedTerm.Length is < 3 or > 100)
        {
            throw new DomainException("Enter the beginning of a name or email (at least 3 characters and no more than 100).");
        }

        return await _store.SearchAssignableVolunteersAsync(
            normalizedTerm.ToUpperInvariant(),
            10,
            cancellationToken);
    }

    public async Task<AuditHistoryPageDto> GetAuditHistoryPageAsync(
        AuditHistoryFilter filter,
        AuditHistoryCursor? cursor,
        CancellationToken cancellationToken)
    {
        var settings = await _store.GetGroupSettingsAsync(cancellationToken);
        var timeZoneId = settings?.TimeZoneId ?? "Etc/UTC";
        var page = await _store.GetAuditHistoryPageAsync(filter, cursor, 50, cancellationToken);
        var timeZone = ResolveTimeZone(timeZoneId);
        var items = page.Items
            .Select(row => ToAuditHistoryItem(row, timeZone))
            .ToArray();
        return new AuditHistoryPageDto(
            items,
            page.HasNextPage,
            page.HasPreviousPage,
            timeZoneId);
    }

    public async Task<IReadOnlyList<AuditActorChoiceDto>> GetAuditActorsAsync(
        CancellationToken cancellationToken)
    {
        var actors = await _store.GetAuditActorsAsync(cancellationToken);
        return actors
            .Where(actor => !string.IsNullOrWhiteSpace(actor))
            .Select(actor => actor.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(HumanizeActor, StringComparer.Ordinal)
            .ThenBy(actor => actor, StringComparer.OrdinalIgnoreCase)
            .Select(actor => new AuditActorChoiceDto(actor, HumanizeActor(actor)))
            .ToArray();
    }

    public Task<IReadOnlyList<CoordinatorFilterChoiceDto>> SearchAuditShiftsAsync(
        string term,
        CancellationToken cancellationToken) =>
        _store.SearchAuditShiftsAsync(term, 10, cancellationToken);

    public async Task<CoordinatorAccessDiagnosticsDto> GetCoordinatorAccessDiagnosticsAsync(
        bool oidcConfigured,
        IReadOnlyCollection<string> allowlistedEmails,
        string? currentEmail,
        CancellationToken cancellationToken)
    {
        var normalizedEmails = allowlistedEmails
            .Select(VolunteerCoordinator.Domain.Volunteers.Volunteer.NormalizeEmail)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var verifications = await _store.GetCoordinatorAccessVerificationsAsync(
            normalizedEmails,
            cancellationToken);
        return new CoordinatorAccessDiagnosticsDto(
            oidcConfigured,
            normalizedEmails,
            string.IsNullOrWhiteSpace(currentEmail)
                ? null
                : VolunteerCoordinator.Domain.Volunteers.Volunteer.NormalizeEmail(currentEmail),
            verifications);
    }

    public async Task VerifyCoordinatorAccessAsync(
        string coordinatorEmail,
        CancellationToken cancellationToken)
    {
        var actor = RequireCoordinator(coordinatorEmail);
        var now = _clock.UtcNow;
        await _store.ExecuteInTransactionAsync(
            token =>
            {
                _store.AddAuditEntry(AuditEntry.Create(
                    now,
                    actor,
                    "CoordinatorAccessVerified",
                    "Coordinator",
                    Guid.Empty,
                    "{}"));
                return Task.FromResult(true);
            },
            cancellationToken);
    }

    private static AuditHistoryItemDto ToAuditHistoryItem(
        AuditHistoryQueryRow row,
        TimeZoneInfo timeZone)
    {
        var actorDisplay = HumanizeActor(row.Actor);
        var volunteerReference = row.VolunteerIsAnonymized && row.VolunteerId.HasValue
            ? "Removed volunteer · reference " + OpaqueReference(row.VolunteerId.Value)
            : null;
        var summary = BuildHumanHistorySummary(row, actorDisplay, timeZone, volunteerReference);
        return new AuditHistoryItemDto(
            row.Id,
            row.OccurredAtUtc,
            actorDisplay,
            row.Category,
            summary,
            volunteerReference);
    }

    private static string BuildHumanHistorySummary(
        AuditHistoryQueryRow row,
        string actorDisplay,
        TimeZoneInfo timeZone,
        string? volunteerReference)
    {
        var shiftContext = row.ShiftTitle is null || !row.ShiftStartsAtUtc.HasValue
            ? null
            : $"{LocalDay(row.ShiftStartsAtUtc.Value, timeZone)}'s {row.ShiftTitle}";
        var person = volunteerReference ?? row.VolunteerName;
        var subject = person is null ? shiftContext : shiftContext is null ? person : $"{person} for {shiftContext}";
        return row.Action switch
        {
            "ShiftPublished" => subject is null ? $"{actorDisplay} published a schedule." : $"{actorDisplay} published {subject}.",
            "ShiftCreated" => subject is null ? $"{actorDisplay} created a schedule entry." : $"{actorDisplay} created {subject}.",
            "ShiftEdited" => subject is null ? $"{actorDisplay} corrected a schedule entry." : $"{actorDisplay} corrected {subject}.",
            "ShiftDeactivated" => subject is null ? $"{actorDisplay} resolved a schedule entry." : $"{actorDisplay} resolved {subject}.",
            "RequestSubmitted" => subject is null ? "A volunteer request was received." : $"A request was received from {subject}.",
            "RequestApproved" => subject is null ? $"{actorDisplay} approved a request." : $"{actorDisplay} approved a request for {subject}.",
            "RequestRejected" => subject is null ? $"{actorDisplay} declined a request." : $"{actorDisplay} declined a request for {subject}.",
            "AssignmentConfirm" or "AssignmentConfirmed" =>
                subject is null ? "A volunteer confirmed an assignment." : $"{subject} confirmed an assignment.",
            "AssignmentDecline" or "AssignmentDeclined" =>
                subject is null ? "A volunteer declined an assignment." : $"{subject} declined an assignment.",
            "AssignmentCancel" or "AssignmentCancelled" =>
                subject is null ? "A volunteer cancelled an assignment." : $"{subject} cancelled an assignment.",
            "CoordinatorAccessVerified" => $"{actorDisplay} verified coordinator access.",
            "VolunteerAnonymized" => volunteerReference is null ? "Volunteer contact data was removed." : $"{volunteerReference} contact data was removed.",
            "NotificationBounced" or "NotificationComplained" =>
                subject is null ? "Message delivery reported a problem." : $"Message delivery reported a problem for {subject}.",
            "NotificationResendRequested" => subject is null ? $"{actorDisplay} requested another message." : $"{actorDisplay} requested another message for {subject}.",
            "RecurringSeriesCreated" => $"{actorDisplay} created a recurring schedule.",
            "RecurringSeriesRevisionCreated" => $"{actorDisplay} revised a recurring schedule.",
            "RecurringOccurrencesPublished" => $"{actorDisplay} published recurring schedule commitments.",
            "RecurringOccurrenceGenerated" => subject is null ? $"{actorDisplay} generated a recurring schedule commitment." : $"{actorDisplay} generated {subject}.",
            "RecurringOccurrenceNeedsReview" => subject is null ? "A recurring schedule needs a local-time decision." : $"A recurring schedule needs a local-time decision for {subject}.",
            "RecurringOccurrenceGapResolved" => subject is null ? $"{actorDisplay} resolved a recurring schedule gap." : $"{actorDisplay} resolved the recurring schedule gap for {subject}.",
            "RecurringOccurrenceSkipped" => subject is null ? $"{actorDisplay} skipped a recurring schedule date." : $"{actorDisplay} skipped the recurring schedule date for {subject}.",
            "RecurringOccurrenceDetached" => subject is null ? $"{actorDisplay} detached a recurring schedule commitment." : $"{actorDisplay} detached {subject}.",
            "RecurringProtectedOccurrenceResolved" => subject is null ? $"{actorDisplay} resolved a protected recurring occurrence." : $"{actorDisplay} resolved the protected occurrence for {subject}.",
            "RecurringCommitmentRequested" => subject is null ? "A volunteer recurring commitment request was received." : $"A recurring request was received from {subject}.",
            "RecurringCommitmentDirectClaimed" => subject is null ? "A volunteer claimed a recurring commitment." : $"{subject} claimed a recurring commitment.",
            "RecurringCommitmentApproved" => subject is null ? $"{actorDisplay} approved a recurring commitment." : $"{actorDisplay} approved a recurring commitment for {subject}.",
            "RecurringCommitmentRequestRejected" => subject is null ? $"{actorDisplay} declined a recurring commitment request." : $"{actorDisplay} declined the recurring request for {subject}.",
            "RecurringCommitmentConfirmed" => subject is null ? "A volunteer confirmed a recurring commitment." : $"{subject} confirmed a recurring commitment.",
            "RecurringCommitmentWithdrawn" => subject is null ? "A recurring commitment was withdrawn." : $"A recurring commitment was withdrawn for {subject}.",
            "RecurringCommitmentHandedOff" => subject is null ? $"{actorDisplay} handed off a recurring commitment." : $"{actorDisplay} handed off {subject}.",
            "RecurringCommitmentReconciled" => $"{actorDisplay} reconciled a recurring commitment.",
            _ when row.Category == "Messages" => subject is null ? "Message delivery changed." : $"Message delivery changed for {subject}.",
            _ when row.Category == "Privacy" => volunteerReference is null ? "Volunteer contact data changed under the privacy lifecycle." : $"{volunteerReference} contact data changed under the privacy lifecycle.",
            _ when row.Category == "Recurring schedules" => $"{actorDisplay} recorded a recurring schedule change.",
            _ when row.Category == "Recurring commitments" => $"{actorDisplay} recorded a recurring commitment change.",
            _ => "A recorded coordinator action occurred."
        };
    }

    private static string HumanizeActor(string actor)
    {
        if (actor.Equals("retention-worker", StringComparison.OrdinalIgnoreCase))
        {
            return "Automated privacy process";
        }

        if (actor.Equals("recurring-worker", StringComparison.OrdinalIgnoreCase))
        {
            return "Recurring schedule process";
        }

        if (actor.Equals("volunteer-token", StringComparison.OrdinalIgnoreCase) ||
            actor.Equals("volunteer-recovery", StringComparison.OrdinalIgnoreCase) ||
            actor.Equals("DIRECT CLAIM", StringComparison.OrdinalIgnoreCase) ||
            actor.StartsWith("volunteer:", StringComparison.OrdinalIgnoreCase))
        {
            return "Volunteer";
        }

        return actor;
    }

    private static string LocalDay(DateTimeOffset instantUtc, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(instantUtc, timeZone).ToString("dddd", System.Globalization.CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string OpaqueReference(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest)[..6];
    }
}
