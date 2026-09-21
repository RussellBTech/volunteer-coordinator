using VolunteerCoordinator.Domain.Assignments;

namespace VolunteerCoordinator.Application.Ports;

public interface ILegacyActionTokenStore
{
    Task<Guid?> GetActionTokenSlotIdAsync(
        byte[] hash,
        CancellationToken cancellationToken);

    Task<ActionToken?> GetActionTokenByHashAsync(
        byte[] hash,
        CancellationToken cancellationToken);
}
