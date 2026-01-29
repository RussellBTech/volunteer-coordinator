using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Authorization;

public class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AdminAuthorizationHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminRequirement requirement)
    {
        var email = context.User.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrEmpty(email))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VsmsDbContext>();

        var isAdmin = await dbContext.AdminUsers
            .AnyAsync(a => a.Email.ToLower() == email.ToLower());

        if (isAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
