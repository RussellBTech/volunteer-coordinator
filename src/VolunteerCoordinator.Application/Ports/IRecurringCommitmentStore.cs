using VolunteerCoordinator.Domain.Commitments;
using VolunteerCoordinator.Domain.Schedules;

namespace VolunteerCoordinator.Application.Ports;

public interface IRecurringCommitmentStore
{
    Task<RecurringCommitmentRequest?> GetRecurringCommitmentRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringCommitmentRequest>> GetRecurringCommitmentRequestsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringCommitmentRequest>> GetPendingRecurringCommitmentRequestsAsync(
        Guid seriesId,
        CancellationToken cancellationToken);

    Task<RecurringCommitment?> GetRecurringCommitmentAsync(
        Guid commitmentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringCommitment>> GetRecurringCommitmentsAsync(
        CancellationToken cancellationToken);
    Task<IReadOnlyList<RecurringCommitment>> GetRecurringCommitmentsForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringCommitment>> GetRecurringCommitmentsForSeriesAsync(
        Guid seriesId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringCommitment>> GetOverlappingRecurringCommitmentsAsync(
        Guid seriesId,
        SlotKind roleKind,
        int rolePosition,
        DateOnly effectiveLocalDate,
        DateOnly endLocalDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringCommitmentOccurrence>> GetRecurringCommitmentOccurrencesAsync(
        Guid commitmentId,
        CancellationToken cancellationToken);
    Task<RecurringCommitmentOccurrence?> GetRecurringCommitmentOccurrenceByAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken);

    Task<RecurringCommitmentOccurrence?> GetRecurringCommitmentOccurrenceAsync(
        Guid commitmentId,
        Guid recurringOccurrenceId,
        CancellationToken cancellationToken);

    Task<RecurringCommitmentCapability?> GetRecurringCommitmentCapabilityByHashAsync(
        byte[] hash,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringCommitmentCapability>> GetActiveRecurringCapabilitiesAsync(
        Guid volunteerId,
        Guid? commitmentId,
        CancellationToken cancellationToken);
    Task LockRecurringCommitmentRequestAsync(Guid requestId, CancellationToken cancellationToken);


    Task LockRecurringCommitmentAsync(Guid commitmentId, CancellationToken cancellationToken);

    Task LockRecurringCommitmentRoleAsync(
        Guid seriesId,
        SlotKind roleKind,
        int rolePosition,
        CancellationToken cancellationToken);

    Task LockRecurringCommitmentOccurrenceAsync(Guid joinId, CancellationToken cancellationToken);

    void AddRecurringCommitmentRequest(RecurringCommitmentRequest request);
    void AddRecurringCommitment(RecurringCommitment commitment);
    void AddRecurringCommitmentOccurrence(RecurringCommitmentOccurrence occurrence);
    void AddRecurringCommitmentCapability(RecurringCommitmentCapability capability);
}
