using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Resend;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Infrastructure.Notifications;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class ResendAdapterContractTests
{
    [Fact]
    public async Task AcceptedSendUsesAuthorizationAndIdempotencyHeaders()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"id\":\"00000000-0000-0000-0000-000000000001\"}");
        var provider = CreateProvider(handler);

        var result = await provider.SendAsync(
            "volunteer@example.org",
            "Volunteer Coordinator <sender@example.org>",
            "help@example.org",
            "notification/attempt-1",
            new EmailTemplate("Subject", "Plain body", "<p>HTML body</p>"),
            default);

        Assert.True(handler.Request is not null, result.Category);
        Assert.True(result.Accepted, result.Category);
        Assert.False(result.Transient);
        Assert.Equal("Bearer test-api-key", handler.Request!.Headers.Authorization?.ToString());
        Assert.Equal("notification/attempt-1", handler.Request.Headers.GetValues("Idempotency-Key").Single());
        var body = await handler.Request.Content!.ReadAsStringAsync();
        Assert.Contains("volunteer@example.org", body, StringComparison.Ordinal);
        Assert.Contains("sender@example.org", body, StringComparison.Ordinal);
        Assert.Contains("help@example.org", body, StringComparison.Ordinal);
        Assert.Contains("Plain body", body, StringComparison.Ordinal);
        Assert.Contains("HTML body", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData((HttpStatusCode)429, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task HttpFailureClassificationSeparatesRetryableAndPermanentResponses(
        HttpStatusCode statusCode,
        bool expectedTransient)
    {
        var errorBody = $"{{\"statusCode\":{(int)statusCode},\"errorType\":\"provider_error\",\"message\":\"provider detail\"}}";
        var provider = CreateProvider(new RecordingHandler(statusCode, errorBody));

        var result = await provider.SendAsync(
            "volunteer@example.org",
            "sender@example.org",
            "help@example.org",
            "notification/attempt-2",
            new EmailTemplate("Subject", "Plain", "<p>HTML</p>"),
            default);

        Assert.False(result.Accepted);
        Assert.True(result.Transient == expectedTransient, result.Category);
        Assert.DoesNotContain("provider detail", result.Category, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RateLimitRetryAfterIsParsedIntoAbsoluteProviderTime()
    {
        var before = DateTimeOffset.UtcNow;
        var provider = CreateProvider(
            new RecordingHandler(
                (HttpStatusCode)429,
                "{\"statusCode\":429,\"errorType\":\"rate_limit\",\"message\":\"limited\"}",
                retryAfterSeconds: 17));

        var result = await provider.SendAsync(
            "volunteer@example.org",
            "sender@example.org",
            "help@example.org",
            "notification/attempt-retry-after",
            new EmailTemplate("Subject", "Plain", "<p>HTML</p>"),
            default);

        Assert.True(result.Transient);
        Assert.NotNull(result.RetryAfterUtc);
        Assert.InRange(
            result.RetryAfterUtc.Value - before,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30));
    }

    private static ResendTransactionalEmailProvider CreateProvider(RecordingHandler handler)
    {
        var options = new ResendClientOptions
        {
            ApiToken = "test-api-key",
            ApiUrl = "https://api.resend.com"
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.resend.com")
        };
        return new ResendTransactionalEmailProvider(
            new ResendClient(new Snapshot<ResendClientOptions>(options), httpClient));
    }

    private sealed class Snapshot<T> : IOptionsSnapshot<T>
        where T : class, new()
    {
        public Snapshot(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public T Get(string? name) => Value;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly int? _retryAfterSeconds;
        public RecordingHandler(
            HttpStatusCode statusCode,
            string responseBody,
            int? retryAfterSeconds = null)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
            _retryAfterSeconds = retryAfterSeconds;
        }
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            var response = new HttpResponseMessage(_statusCode)
            {
                RequestMessage = request,
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            if (_retryAfterSeconds is int retryAfterSeconds)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(retryAfterSeconds));
            }
            return Task.FromResult(response);
        }
    }
}
