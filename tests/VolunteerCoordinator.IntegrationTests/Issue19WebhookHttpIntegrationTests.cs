using System.Text.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

using VolunteerCoordinator.Infrastructure.Notifications;
namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue19WebhookHttpIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlFixture _fixture;

    public Issue19WebhookHttpIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HttpWebhookAuthenticatesDeduplicatesAndKeepsTerminalOrdering()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(Now);
        const string providerMessageId = "msg-webhook";
        var secret = Convert.ToBase64String(Encoding.UTF8.GetBytes("local-webhook-secret"));
        await using (var context = _fixture.CreateContext())
        {
            var service = ScheduleTestHelpers.CreateService(context, clock);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Webhook shift",
                null,
                null,
                Now.AddDays(2),
                Now.AddDays(2).AddHours(1),
                0,
                "coordinator@example.org");
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, "coordinator@example.org", default);
            var submission = await service.SubmitRequestAsync(
                shift.Slots.Single().Id,
                "Webhook volunteer",
                "webhook@example.org",
                null,
                default);
            var intent = await context.NotificationIntents.SingleAsync(x => x.Id == submission.RequestId || x.Kind == "RequestReceipt");
            intent.Accept(Now, providerMessageId);
            await context.SaveChangesAsync();
        }

        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            clock: clock,
            webhookSecret: secret);
        Assert.Equal(
            secret,
            factory.Services.GetRequiredService<IOptions<ResendOptions>>().Value.WebhookSecret);
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var readiness = await client.GetAsync("/health/ready");
            if (readiness.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(100);
        }

        var invalidBody = WebhookBody("email.delivered", providerMessageId, Now);
        var invalid = await SendWebhookAsync(client, invalidBody, "invalid-id", secret, includeSignature: false);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var deliveredBody = WebhookBody("email.delivered", providerMessageId, Now);
        var delivered = await SendWebhookAsync(client, deliveredBody, "svix-delivered", secret);
        Assert.True(
            delivered.StatusCode == HttpStatusCode.OK,
            $"Webhook delivery failed with {delivered.StatusCode}: {await delivered.Content.ReadAsStringAsync()}");
        var duplicate = await SendWebhookAsync(client, deliveredBody, "svix-delivered", secret);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

        var bouncedBody = WebhookBody("email.bounced", providerMessageId, Now.AddMinutes(1));
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWebhookAsync(client, bouncedBody, "svix-bounced", secret)).StatusCode);
        var lateDeliveredBody = WebhookBody("email.delivered", providerMessageId, Now.AddSeconds(-1));
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWebhookAsync(client, lateDeliveredBody, "svix-late-delivered", secret)).StatusCode);

        await using var verification = _fixture.CreateContext();
        var intentState = await verification.NotificationIntents
            .Where(x => x.ProviderMessageId == providerMessageId)
            .Select(x => x.State)
            .SingleAsync();
        Assert.Equal(Domain.Notifications.NotificationIntentState.Bounced, intentState);
        Assert.Equal(3, await verification.ResendWebhookReceipts.CountAsync());
        Assert.DoesNotContain(
            "webhook@example.org",
            await verification.ResendWebhookReceipts.Select(x => x.ProviderMessageId).ToListAsync());
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string body,
        string svixId,
        string secret,
        bool includeSignature = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("svix-id", svixId);
        var timestamp = Now.ToUnixTimeSeconds().ToString();
        request.Headers.Add("svix-timestamp", timestamp);
        if (includeSignature)
        {
            var key = Convert.FromBase64String(secret);
            var signed = Encoding.UTF8.GetBytes($"{svixId}.{timestamp}.{body}");
            var signature = Convert.ToBase64String(HMACSHA256.HashData(key, signed));
            request.Headers.Add("svix-signature", $"v1,{signature}");
        }

        return await client.SendAsync(request);
    }

    private static string WebhookBody(
        string eventType,
        string providerMessageId,
        DateTimeOffset occurredAtUtc) =>
        JsonSerializer.Serialize(new
        {
            type = eventType,
            created_at = occurredAtUtc,
            data = new { email_id = providerMessageId }
        });
}
