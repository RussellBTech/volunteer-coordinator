using System.Collections.Concurrent;
using VolunteerCoordinator.Application.Notifications;

namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class TransientLinkMaterialStore : ITransientLinkMaterialStore
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public void PutHubToken(Guid intentId, string rawToken, DateTimeOffset expiresAtUtc) =>
        _entries[intentId] = new(rawToken, expiresAtUtc);

    public bool TryTakeHubToken(Guid intentId, DateTimeOffset nowUtc, out string? rawToken)
    {
        rawToken = null;
        if (!_entries.TryRemove(intentId, out var entry) || entry.ExpiresAtUtc < nowUtc)
        {
            return false;
        }

        rawToken = entry.RawToken;
        return true;
    }

    private sealed record Entry(string RawToken, DateTimeOffset ExpiresAtUtc);
}
