using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Ports;

public interface IRecurringShiftStore
{
    Task<IReadOnlyList<RecurringShiftSeries>> GetRecurringSeriesAsync(CancellationToken cancellationToken);

    Task<RecurringShiftSeries?> GetRecurringSeriesAsync(Guid seriesId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetActiveRecurringSeriesIdsAsync(CancellationToken cancellationToken);

    Task<int> CountZoneReviewSeriesAsync(string groupTimeZoneId, CancellationToken cancellationToken);
    Task<RecurringShiftSeriesRevision?> GetRecurringRevisionAsync(
        Guid revisionId,
        CancellationToken cancellationToken);


    Task<IReadOnlyList<RecurringShiftSeriesRevision>> GetRecurringRevisionsAsync(
        Guid seriesId,
        CancellationToken cancellationToken);

    Task<RecurringShiftSeriesRevision?> GetApplicableRecurringRevisionAsync(
        Guid seriesId,
        DateOnly localDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringShiftOccurrence>> GetRecurringOccurrencesAsync(
        Guid seriesId,
        DateOnly? fromLocalDate,
        DateOnly? throughLocalDate,
        CancellationToken cancellationToken);

    Task<RecurringShiftOccurrence?> GetRecurringOccurrenceAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken);

    Task<RecurringShiftOccurrence?> GetRecurringOccurrenceByShiftAsync(
        Guid shiftId,
        CancellationToken cancellationToken);

    Task LockRecurringSeriesAsync(Guid seriesId, CancellationToken cancellationToken);

    Task LockRecurringOccurrenceAsync(Guid occurrenceId, CancellationToken cancellationToken);

    void AddRecurringSeries(RecurringShiftSeries series);

    void AddRecurringRevision(RecurringShiftSeriesRevision revision);

    void AddRecurringOccurrence(RecurringShiftOccurrence occurrence);
}
