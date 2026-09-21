namespace VolunteerCoordinator.Application.Notifications;

public interface ITransientLinkMaterialStore
{
    void PutHubToken(Guid intentId, string rawToken, DateTimeOffset expiresAtUtc);

    bool TryTakeHubToken(Guid intentId, DateTimeOffset nowUtc, out string? rawToken);
}
