namespace VolunteerCoordinator.Application.Notifications;

public interface ITransactionalEmailProvider
{
    Task<ProviderDeliveryResult> SendAsync(
        string recipient,
        string from,
        string replyTo,
        string idempotencyKey,
        EmailTemplate template,
        CancellationToken cancellationToken);
}
