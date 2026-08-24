using System.Security.Claims;

namespace VolunteerCoordinator.Web.Security;

public static class CoordinatorIdentity
{
    public static string? GetEmail(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");
        var emailVerified = principal.FindFirstValue("email_verified");
        if (!string.Equals(emailVerified, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();
    }
}
