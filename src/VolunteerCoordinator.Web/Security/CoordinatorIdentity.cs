using System.Security.Claims;

namespace VolunteerCoordinator.Web.Security;

public static class CoordinatorIdentity
{
    public static string? GetEmail(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? principal.FindFirstValue("preferred_username");
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();
    }
}
