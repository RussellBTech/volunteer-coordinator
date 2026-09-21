using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using VolunteerCoordinator.Application.Models;

namespace VolunteerCoordinator.Web.Security;

public sealed class CoordinatorCursorProtector
{
    private readonly IDataProtector _protector;

    public CoordinatorCursorProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("VolunteerCoordinator.CoordinatorCursors.v1");
    }

    public string Fingerprint(params string?[] values)
    {
        var canonical = string.Join("\u001f", values.Select(value => value ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public string ProtectWork(CoordinatorWorkCursor cursor, string fingerprint)
    {
        var payload = JsonSerializer.Serialize(new WorkPayload(
            fingerprint,
            cursor.SeverityRank,
            cursor.DueAtUtc,
            cursor.CategoryRank,
            cursor.StableId,
            cursor.Forward));
        return Protect(payload);
    }

    public bool TryUnprotectWork(
        string? protectedCursor,
        string fingerprint,
        out CoordinatorWorkCursor? cursor)
    {
        cursor = null;
        if (!TryUnprotect(protectedCursor, out var json))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<WorkPayload>(json);
            if (payload is null || !string.Equals(payload.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            cursor = new CoordinatorWorkCursor(
                payload.SeverityRank,
                payload.DueAtUtc,
                payload.CategoryRank,
                payload.StableId,
                payload.Forward);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string ProtectHistory(AuditHistoryCursor cursor, string fingerprint)
    {
        var payload = JsonSerializer.Serialize(new HistoryPayload(
            fingerprint,
            cursor.OccurredAtUtc,
            cursor.AuditId,
            cursor.Forward));
        return Protect(payload);
    }

    public bool TryUnprotectHistory(
        string? protectedCursor,
        string fingerprint,
        out AuditHistoryCursor? cursor)
    {
        cursor = null;
        if (!TryUnprotect(protectedCursor, out var json))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<HistoryPayload>(json);
            if (payload is null || !string.Equals(payload.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            cursor = new AuditHistoryCursor(
                payload.OccurredAtUtc,
                payload.AuditId,
                payload.Forward);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string Protect(string value) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(_protector.Protect(value)));

    private bool TryUnprotect(string? value, out string plaintext)
    {
        plaintext = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            plaintext = _protector.Unprotect(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)));
            return true;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            return false;
        }
    }

    private sealed record WorkPayload(
        string Fingerprint,
        int SeverityRank,
        DateTimeOffset DueAtUtc,
        int CategoryRank,
        Guid StableId,
        bool Forward);

    private sealed record HistoryPayload(
        string Fingerprint,
        DateTimeOffset OccurredAtUtc,
        Guid AuditId,
        bool Forward);
}
