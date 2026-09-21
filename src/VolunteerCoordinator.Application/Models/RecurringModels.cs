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
    uint ExpectedSettingsVersion);

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
    uint ExpectedSettingsVersion = 0);

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
    bool IsZoneReviewRequired = false);

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
    uint Version);

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
    bool IsZoneAdoption = false);

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
