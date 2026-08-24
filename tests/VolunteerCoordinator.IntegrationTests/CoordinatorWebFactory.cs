using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class CoordinatorWebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly bool _authenticateNonCoordinator;

    public CoordinatorWebFactory(string connectionString, bool authenticateNonCoordinator = false)
    {
        _connectionString = connectionString;
        _authenticateNonCoordinator = authenticateNonCoordinator;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("DevelopmentAuth:Enabled", "true");
        builder.UseSetting("Coordinator:AllowedEmails:0", "coordinator@example.org");

        if (_authenticateNonCoordinator)
        {
            builder.ConfigureTestServices(services =>
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.AuthenticationSchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.AuthenticationSchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.AuthenticationSchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.AuthenticationSchemeName,
                        _ => { }));
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string AuthenticationSchemeName = "AuthenticatedNonCoordinator";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "not-allowed@example.org"),
                    new Claim(ClaimTypes.Email, "not-allowed@example.org"),
                    new Claim("email_verified", bool.TrueString)
                ],
                AuthenticationSchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AuthenticationSchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
