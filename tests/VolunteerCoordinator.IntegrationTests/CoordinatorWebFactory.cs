using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Web.Security;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class CoordinatorWebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly bool _authenticateNonCoordinator;
    private readonly IReadOnlyList<string> _allowedEmails;
    private readonly AnonymousRateLimitOptions? _rateLimits;
    private readonly IClock? _clock;
    private readonly ITransactionalEmailProvider? _emailProvider;
    private readonly string? _webhookSecret;
    private readonly ILoggerProvider? _loggerProvider;

    public CoordinatorWebFactory(
        string connectionString,
        bool authenticateNonCoordinator = false,
        IReadOnlyList<string>? allowedEmails = null,
        AnonymousRateLimitOptions? rateLimits = null,
        IClock? clock = null,
        ITransactionalEmailProvider? emailProvider = null,
        string? webhookSecret = null,
        ILoggerProvider? loggerProvider = null)
    {
        _connectionString = connectionString;
        _authenticateNonCoordinator = authenticateNonCoordinator;
        _allowedEmails = allowedEmails ?? ["coordinator@example.org"];
        _rateLimits = rateLimits;
        _clock = clock;
        _emailProvider = emailProvider;
        _webhookSecret = webhookSecret;
        _loggerProvider = loggerProvider;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
        builder.UseSetting("DevelopmentAuth:Enabled", "true");
        builder.UseSetting("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");
        for (var index = 0; index < _allowedEmails.Count; index++)
        {
            builder.UseSetting($"Coordinator:AllowedEmails:{index}", _allowedEmails[index]);
        }

        if (_webhookSecret is not null)
        {
            builder.UseSetting("Resend:WebhookSecret", _webhookSecret);
        }

        builder.ConfigureTestServices(services =>
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

            if (_clock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            }

            if (_emailProvider is not null)
            {
                services.RemoveAll<ITransactionalEmailProvider>();
                services.AddSingleton(_emailProvider);
            }
            if (_loggerProvider is not null)
            {
                services.AddLogging(logging => logging.AddProvider(_loggerProvider));
            }
        });
        if (_rateLimits is not null)
        {
            builder.UseSetting(
                "AnonymousRateLimits:RequestMutation:PermitLimit",
                _rateLimits.RequestMutation.PermitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting(
                "AnonymousRateLimits:RequestMutation:Window",
                _rateLimits.RequestMutation.Window.ToString("c", CultureInfo.InvariantCulture));
            builder.UseSetting(
                "AnonymousRateLimits:PrivateTokenRead:PermitLimit",
                _rateLimits.PrivateTokenRead.PermitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting(
                "AnonymousRateLimits:PrivateTokenRead:Window",
                _rateLimits.PrivateTokenRead.Window.ToString("c", CultureInfo.InvariantCulture));
            builder.UseSetting(
                "AnonymousRateLimits:AssignmentActionMutation:PermitLimit",
                _rateLimits.AssignmentActionMutation.PermitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting(
                "AnonymousRateLimits:AssignmentActionMutation:Window",
                _rateLimits.AssignmentActionMutation.Window.ToString("c", CultureInfo.InvariantCulture));
        }

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
