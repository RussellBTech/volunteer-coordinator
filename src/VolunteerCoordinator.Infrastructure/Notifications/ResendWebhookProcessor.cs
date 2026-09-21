using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Domain.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class ResendWebhookProcessor
{
    private readonly VolunteerCoordinatorDbContext _db;
    private readonly IClock _clock;
    private readonly IOptions<ResendOptions> _options;
    private readonly IOptions<ResendWebhookOptions> _webhookOptions;

    public ResendWebhookProcessor(
        VolunteerCoordinatorDbContext db,
        IClock clock,
        IOptions<ResendOptions> options,
        IOptions<ResendWebhookOptions> webhookOptions)
    {
        _db = db;
        _clock = clock;
        _options = options;
        _webhookOptions = webhookOptions;
    }

    public async Task<bool> ProcessAsync(
        string rawBody,
        string? svixId,
        string? svixTimestamp,
        string? svixSignature,
        CancellationToken cancellationToken)
    {
        if (!Verify(rawBody, svixId, svixTimestamp, svixSignature))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            var eventType = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            var data = root.TryGetProperty("data", out var dataElement) ? dataElement : default;
            var providerMessageId = data.ValueKind == JsonValueKind.Object &&
                                    data.TryGetProperty("email_id", out var emailIdElement)
                ? emailIdElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(svixId) ||
                string.IsNullOrWhiteSpace(eventType) ||
                string.IsNullOrWhiteSpace(providerMessageId))
            {
                return false;
            }

            var occurredAt = ParseOccurrence(root, _clock.UtcNow);
            if (eventType is not ("email.delivered" or "email.bounced" or "email.complained"))
            {
                return true;
            }

            var processedAt = _clock.UtcNow;
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var inserted = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "ResendWebhookReceipts"
                    ("Id", "SvixId", "EventType", "ProviderMessageId",
                     "ProviderOccurredAtUtc", "ProcessedAtUtc")
                VALUES
                    ({Guid.NewGuid()}, {svixId}, {eventType}, {providerMessageId},
                     {occurredAt}, {processedAt})
                ON CONFLICT ("SvixId") DO NOTHING
                """,
                cancellationToken);
            if (inserted == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }

            _db.ChangeTracker.Clear();
            var intent = await _db.NotificationIntents
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM "NotificationIntents"
                    WHERE "ProviderMessageId" = {providerMessageId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            intent?.ApplyWebhook(eventType, occurredAt, processedAt);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
    }

    private bool Verify(
        string rawBody,
        string? svixId,
        string? svixTimestamp,
        string? svixSignature)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.WebhookSecret) ||
            string.IsNullOrWhiteSpace(rawBody) ||
            string.IsNullOrWhiteSpace(svixId) ||
            string.IsNullOrWhiteSpace(svixTimestamp) ||
            string.IsNullOrWhiteSpace(svixSignature) ||
            !long.TryParse(svixTimestamp, out var unixSeconds))
        {
            return false;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Math.Abs((_clock.UtcNow - timestamp).TotalSeconds) >
            _webhookOptions.Value.TimestampTolerance.TotalSeconds)
        {
            return false;
        }

        var secret = _options.Value.WebhookSecret.Trim();
        if (secret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            secret = secret[6..];
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var signed = Encoding.UTF8.GetBytes($"{svixId}.{svixTimestamp}.{rawBody}");
        var expected = Convert.ToBase64String(HMACSHA256.HashData(key, signed));
        return svixSignature
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split(',', 2))
            .Where(value => value.Length == 2 && value[0] == "v1")
            .Select(value => value[1])
            .Any(value => CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(value),
                Encoding.UTF8.GetBytes(expected)));
    }

    private static DateTimeOffset ParseOccurrence(JsonElement root, DateTimeOffset fallback)
    {
        if (root.TryGetProperty("created_at", out var created) &&
            created.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(created.GetString(), out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return fallback;
    }
}
