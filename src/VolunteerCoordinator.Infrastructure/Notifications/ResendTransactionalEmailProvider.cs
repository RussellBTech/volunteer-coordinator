using System.Net;
using Resend;
using VolunteerCoordinator.Application.Notifications;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class ResendTransactionalEmailProvider : ITransactionalEmailProvider
{
    private readonly IResend _resend;

    public ResendTransactionalEmailProvider(IResend resend)
    {
        _resend = resend;
    }

    public async Task<ProviderDeliveryResult> SendAsync(
        string recipient,
        string from,
        string replyTo,
        string idempotencyKey,
        EmailTemplate template,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = new EmailMessage
            {
                From = EmailAddress.Parse(from),
                ReplyTo = new EmailAddressList { EmailAddress.Parse(replyTo) },
                Subject = template.Subject,
                TextBody = template.TextBody,
                HtmlBody = template.HtmlBody
            };
            message.To.Add(EmailAddress.Parse(recipient));
            var result = await _resend.EmailSendAsync(idempotencyKey, message, cancellationToken);
            if (result.Success)
            {
                return new ProviderDeliveryResult(true, false, "Accepted", result.Content.ToString());
            }

            var exception = result.Exception;
            var statusCode = exception?.StatusCode;
            var transient = exception?.IsTransient == true || statusCode is
                HttpStatusCode.RequestTimeout or
                HttpStatusCode.Conflict or
                (HttpStatusCode)429 or
                HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout;
            return new ProviderDeliveryResult(
                false,
                transient,
                transient ? "ProviderTransientFailure" : "ProviderPermanentFailure",
                null,
                RetryAfterUtc(exception));
        }
        catch (ResendException exception)
        {
            var transient = exception.IsTransient || exception.StatusCode is
                HttpStatusCode.RequestTimeout or
                HttpStatusCode.Conflict or
                (HttpStatusCode)429 or
                HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout;
            return new ProviderDeliveryResult(
                false,
                transient,
                transient ? "ProviderTransientFailure" : "ProviderPermanentFailure",
                null,
                RetryAfterUtc(exception));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderDeliveryResult(false, true, "Timeout");
        }
        catch (HttpRequestException)
        {
            return new ProviderDeliveryResult(false, true, "ConnectionFailure");
        }
    }
    private static DateTimeOffset? RetryAfterUtc(ResendException? exception)
    {
        var retryAfterSeconds = exception?.Limits?.RetryAfter;
        if (retryAfterSeconds is not > 0)
        {
            return null;
        }

        return DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds.Value);
    }

}

public sealed class UnavailableTransactionalEmailProvider : ITransactionalEmailProvider
{
    public Task<ProviderDeliveryResult> SendAsync(
        string recipient,
        string from,
        string replyTo,
        string idempotencyKey,
        EmailTemplate template,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderDeliveryResult(false, true, "ProviderUnavailable"));
}
