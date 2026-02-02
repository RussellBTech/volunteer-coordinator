using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Authorization;

public class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AdminAuthorizationHandler(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminRequirement requirement)
    {
        // Development bypass: allow admin access when Google OAuth is not configured
        var googleClientId = _configuration["Google:ClientId"];
        var googleClientSecret = _configuration["Google:ClientSecret"];
        var hasGoogleAuth = !string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret);

        if (_environment.IsDevelopment() && !hasGoogleAuth)
        {
            // Auto-succeed in development when Google auth is not configured
            context.Succeed(requirement);
            return;
        }

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
