using VolunteerCoordinator.Domain.Commitments;
using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Models;

public sealed record RecurringSeriesInput(
    string Title,
    string? Location,
    string? VolunteerInstructions,
    string? InternalCoordinatorNotes,
    RecurrenceKind RecurrenceKind,
    int Interval,
    IReadOnlyCollection<DayOfWeek> Weekdays,
    DateOnly AnchorLocalDate,
    TimeOnly LocalStartTime,
    int DurationMinutes,
    int BackupSlotCount,
    int HorizonWeeks,
    AmbiguousTimeChoice AmbiguousTimeChoice,
    uint ExpectedSettingsVersion,
    SignupPolicy SignupPolicy = SignupPolicy.ApprovalRequired);

public sealed record RecurringRevisionInput(
    DateOnly EffectiveLocalDate,
    string Title,
    string? Location,
    string? VolunteerInstructions,
    string? InternalCoordinatorNotes,
    RecurrenceKind RecurrenceKind,
    int Interval,
    IReadOnlyCollection<DayOfWeek> Weekdays,
    DateOnly AnchorLocalDate,
    TimeOnly LocalStartTime,
    int DurationMinutes,
    int BackupSlotCount,
    int HorizonWeeks,
    string TimeZoneId,
    AmbiguousTimeChoice AmbiguousTimeChoice,
    uint ExpectedSeriesVersion,
    string? ExpectedClassification = null,
    uint ExpectedSettingsVersion = 0,
    SignupPolicy SignupPolicy = SignupPolicy.ApprovalRequired,
    bool ConfirmPolicyChange = false,
    SignupPolicy? ExpectedCurrentPolicy = null);

public sealed record RecurringOccurrencePreviewDto(
    Guid? OccurrenceId,
    DateOnly LocalDate,
    DateTime LocalStart,
    DateTime? LocalEnd,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    string Status,
    string DstStatus,
    string? Offset,
    bool IsPublished,
    bool IsException,
    string? ProtectionSummary = null)
{
    public IReadOnlyList<string> RoleLabels { get; init; } = [];
}

public sealed record RecurringSeriesPreviewDto(
    string TimeZoneId,
    RecurrenceKind RecurrenceKind,
    int Interval,
    IReadOnlyList<DayOfWeek> Weekdays,
    DateOnly AnchorLocalDate,
    TimeOnly LocalStartTime,
    int DurationMinutes,
    int HorizonWeeks,
    AmbiguousTimeChoice AmbiguousTimeChoice,
    IReadOnlyList<RecurringOccurrencePreviewDto> Occurrences,
    int NeedsReviewCount,
    int ProtectedCount,
    uint ExpectedSettingsVersion,
    string ExpectedClassification,
    bool IsZoneReviewRequired = false,
    SignupPolicy SignupPolicy = SignupPolicy.ApprovalRequired);

public sealed record RecurringSeriesSummaryDto(
    Guid Id,
    bool IsActive,
    int CurrentRevisionNumber,
    string Title,
    string RecurrenceDescription,
    string TimeZoneId,
    int NeedsReviewCount,
    int UnpublishedCount,
    int ProtectedExceptionCount,
    int SkippedCount,
    bool IsZoneReviewRequired,
    uint Version,
    SignupPolicy SignupPolicy = SignupPolicy.ApprovalRequired);

public sealed record RecurringOccurrenceReviewDto(
    Guid OccurrenceId,
    Guid SeriesId,
    DateOnly LocalDate,
    TimeOnly SuggestedLocalStart,
    string TimeZoneId,
    int DurationMinutes,
    string Status,
    string DstStatus,
    uint Version,
    string? ResolutionReason);

public sealed record RecurringProtectedResolutionPreviewDto(
    Guid OccurrenceId,
    Guid SeriesId,
    DateOnly LocalDate,
    IReadOnlyList<string> AffectedPeople,
    int PendingRequestCount,
    int ActiveAssignmentCount,
    uint ExpectedOccurrenceVersion,
    uint ExpectedSeriesVersion);

public sealed record RecurringSeriesDetailDto(
    RecurringSeriesSummaryDto Series,
    IReadOnlyList<RecurringOccurrencePreviewDto> NeedsReview,
    IReadOnlyList<RecurringOccurrencePreviewDto> Unpublished,
    IReadOnlyList<RecurringOccurrencePreviewDto> Published,
    IReadOnlyList<RecurringOccurrencePreviewDto> ProtectedExceptions,
    IReadOnlyList<RecurringOccurrencePreviewDto> Skipped,
    uint ExpectedSeriesVersion);

public sealed record RecurringRevisionRowDto(
    Guid OccurrenceId,
    DateOnly LocalDate,
    string Classification,
    string? ProtectionSummary,
    uint OccurrenceVersion);

public sealed record RecurringRevisionPreviewDto(
    Guid SeriesId,
    DateOnly EffectiveLocalDate,
    IReadOnlyList<RecurringRevisionRowDto> Rows,
    int EligibleCount,
    int ProtectedCount,
    int ExceptionCount,
    int NeedsReviewCount,
    int SkippedCount,
    uint ExpectedSeriesVersion,
    string ExpectedClassification,
    bool IsZoneAdoption = false,
    SignupPolicy CurrentPolicy = SignupPolicy.ApprovalRequired,
    SignupPolicy ProposedPolicy = SignupPolicy.ApprovalRequired,
    string PolicyConsequence = "");

public sealed record RecurringPublicationBlockerDto(DateOnly LocalDate, string Reason);

public sealed record RecurringPublicationPreviewDto(
    Guid SeriesId,
    DateOnly FromLocalDate,
    DateOnly ThroughLocalDate,
    IReadOnlyList<RecurringOccurrencePreviewDto> Occurrences,
    IReadOnlyList<RecurringPublicationBlockerDto> Blockers,
    string ExpectedVersions,
    uint ExpectedSeriesVersion);

public sealed record RecurringCommandResult(int ChangedCount, IReadOnlyList<RecurringPublicationBlockerDto> Blockers);

public sealed record RecurringCommitmentDateDto(
    Guid JoinId,
    Guid OccurrenceId,
    DateOnly LocalDate,
    DateTimeOffset StartsAtUtc,
    string RoleLabel,
    string State,
    string? Reason,
    Guid? AssignmentId);

public sealed record RecurringCommitmentPreviewDto(
    Guid SeriesId,
    Guid RevisionId,
    string Title,
    SignupPolicy SignupPolicy,
    string PolicyConsequence,
    SlotKind RoleKind,
    int RolePosition,
    string RoleLabel,
    DateOnly EffectiveLocalDate,
    DateOnly EndLocalDate,
    int HorizonWeeks,
    IReadOnlyList<RecurringCommitmentDateDto> Dates,
    int IncludedCount,
    int SkippedCount,
    uint ExpectedSeriesVersion);

public sealed record RecurringCommitmentHandoffRowDto(
    Guid JoinId,
    Guid OldOccurrenceId,
    DateOnly OldLocalDate,
    Guid? NewOccurrenceId,
    DateOnly? NewLocalDate,
    string State,
    string? Reason,
    IReadOnlyList<Guid>? AvailableTargetOccurrenceIds = null,
    uint OldJoinVersion = 0,
    uint OldOccurrenceVersion = 0,
    Guid? OldRevisionId = null,
    uint? OldShiftVersion = null,
    DateTimeOffset? OldStartsAtUtc = null,
    uint? NewOccurrenceVersion = null,
    Guid? NewRevisionId = null,
    uint? NewShiftVersion = null,
    DateTimeOffset? NewStartsAtUtc = null);

public sealed record RecurringCommitmentHandoffPreviewDto(
    Guid CommitmentId,
    Guid SeriesId,
    DateOnly EffectiveLocalDate,
    IReadOnlyList<RecurringCommitmentHandoffRowDto> Rows,
    int MoveCount,
    int SkipCount,
    string ExpectedMapping,
    uint ExpectedCommitmentVersion = 0,
    uint ExpectedSeriesVersion = 0);

public sealed record RecurringCommitmentSubmission(
    Guid RequestId,
    Guid CommitmentId,
    string StatusToken,
    string? NotificationWarning);

public sealed record RecurringCommitmentHubDto(
    Guid CapabilityId,
    Guid? RequestId,
    Guid CommitmentId,
    string VolunteerName,
    string Title,
    string Status,
    SignupPolicy SourcePolicy,
    string StatusMessage,
    DateOnly EffectiveLocalDate,
    DateOnly EndLocalDate,
    IReadOnlyList<RecurringCommitmentDateDto> Dates,
    bool CanConfirm,
    bool CanWithdraw,
    IReadOnlyList<DateOnly> WithdrawalDates);

public sealed record RecurringCommitmentRequestDto(
    Guid RequestId,
    string VolunteerName,
    string VolunteerEmail,
    string Title,
    string RoleLabel,
    SignupPolicy SourcePolicy,
    string Status,
    DateOnly EffectiveLocalDate,
    DateOnly EndLocalDate,
    int IncludedCount,
    int SkippedCount,
    bool CanApprove,
    Guid? RecurringCommitmentId = null);
