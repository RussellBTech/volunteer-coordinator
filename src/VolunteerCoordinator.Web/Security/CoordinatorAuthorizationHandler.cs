using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace VolunteerCoordinator.Web.Security;

public sealed class CoordinatorAuthorizationHandler : AuthorizationHandler<CoordinatorRequirement>
{
    private readonly IOptionsMonitor<CoordinatorOptions> _options;

    public CoordinatorAuthorizationHandler(IOptionsMonitor<CoordinatorOptions> options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CoordinatorRequirement requirement)
    {
        var email = CoordinatorIdentity.GetEmail(context.User);
        if (email is not null && _options.CurrentValue.AllowedEmails.Any(
                allowed => string.Equals(allowed.Trim(), email, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
