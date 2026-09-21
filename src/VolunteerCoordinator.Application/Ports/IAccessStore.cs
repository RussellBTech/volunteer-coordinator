using VolunteerCoordinator.Domain.Access;
using VolunteerCoordinator.Domain.Requests;

namespace VolunteerCoordinator.Application.Ports;

public interface IAccessStore
{
    void AddCapability(VolunteerAccessCapability capability);

    void AddRecoveryToken(RecoveryToken token);

    Task<Guid?> GetCapabilitySlotIdByHashAsync(byte[] hash, CancellationToken cancellationToken);

    Task<VolunteerAccessCapability?> GetCapabilityByHashAsync(byte[] hash, CancellationToken cancellationToken);

    Task<IReadOnlyList<VolunteerAccessCapability>> GetActiveCapabilitiesAsync(
        Guid volunteerId,
        Guid shiftSlotId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VolunteerAccessCapability>> GetCapabilitiesForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecoveryToken>> GetRecoveryTokensForVolunteerAsync(
        Guid volunteerId,
        CancellationToken cancellationToken);

    Task<Guid?> GetRecoverySlotIdByHashAsync(byte[] hash, CancellationToken cancellationToken);

    Task<RecoveryToken?> GetRecoveryTokenByHashAsync(byte[] hash, CancellationToken cancellationToken);

    Task<ShiftRequest?> GetRequestForVolunteerSlotAsync(
        Guid volunteerId,
        Guid shiftSlotId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ShiftRequest>> GetRequestsForVolunteerSlotAsync(
        Guid volunteerId,
        Guid shiftSlotId,
        CancellationToken cancellationToken);
}
