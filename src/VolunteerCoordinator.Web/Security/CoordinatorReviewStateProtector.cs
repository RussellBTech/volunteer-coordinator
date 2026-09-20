using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace VolunteerCoordinator.Web.Security;

public sealed class CoordinatorReviewStateProtector
{
    private const string Purpose = "VolunteerCoordinator.CoordinatorAssignmentReview.v1";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);
    private readonly ITimeLimitedDataProtector _protector;

    public CoordinatorReviewStateProtector(IDataProtectionProvider provider)
    {
        _protector = provider
            .CreateProtector(Purpose)
            .ToTimeLimitedDataProtector();
    }

    public string Protect(CoordinatorAssignmentReviewState state) =>
        Protect(state, DefaultLifetime);

    public string Protect(CoordinatorAssignmentReviewState state, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var payload = JsonSerializer.Serialize(state);
        return _protector.Protect(payload, lifetime);
    }

    public bool TryUnprotect(
        string? protectedState,
        out CoordinatorAssignmentReviewState? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(protectedState))
        {
            return false;
        }

        try
        {
            var payload = _protector.Unprotect(protectedState);
            var candidate = JsonSerializer.Deserialize<CoordinatorAssignmentReviewState>(payload);
            if (candidate is null || !IsValid(candidate))
            {
                return false;
            }

            state = candidate;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValid(CoordinatorAssignmentReviewState state) =>
        state.SlotId != Guid.Empty &&
        (state.ActionKey is "assign" or "replace") &&
        (state.Mode is "known" or "new") &&
        !string.IsNullOrWhiteSpace(state.ExpectedAffectedSet) &&
        (state.Mode != "known" || state.KnownVolunteerId.HasValue) &&
        (state.Mode != "new" ||
         (!string.IsNullOrWhiteSpace(state.VolunteerName) &&
          !string.IsNullOrWhiteSpace(state.VolunteerEmail)));
}
