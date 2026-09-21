using System.Collections.Concurrent;
using VolunteerCoordinator.Application.Notifications;

namespace VolunteerCoordinator.IntegrationTests;

internal sealed class FakeTransactionalEmailProvider : ITransactionalEmailProvider
{
    private readonly ConcurrentQueue<FakeEmailMessage> _messages = new();
    private readonly ConcurrentQueue<ProviderDeliveryResult> _results = new();

    public IReadOnlyCollection<FakeEmailMessage> Messages => _messages.ToArray();

    public void EnqueueResult(ProviderDeliveryResult result) => _results.Enqueue(result);

    public Task<ProviderDeliveryResult> SendAsync(
        string recipient,
        string from,
        string replyTo,
        string idempotencyKey,
        EmailTemplate template,
        CancellationToken cancellationToken)
    {
        _messages.Enqueue(new FakeEmailMessage(
            recipient,
            from,
            replyTo,
            idempotencyKey,
            template));
        if (_results.TryDequeue(out var result))
        {
            return Task.FromResult(result);
        }

        return Task.FromResult(new ProviderDeliveryResult(
            true,
            false,
            "Accepted",
            $"fake-{idempotencyKey.Replace("/", "-", StringComparison.Ordinal)}"));
    }
}

internal sealed record FakeEmailMessage(
    string Recipient,
    string From,
    string ReplyTo,
    string IdempotencyKey,
    EmailTemplate Template);
