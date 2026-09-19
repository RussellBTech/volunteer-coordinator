using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VolunteerCoordinator.Infrastructure.DependencyInjection;
using VolunteerCoordinator.Infrastructure.Health;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Web.Security;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services.AddVolunteerCoordinatorInfrastructure(connectionString);
builder.Services.AddRazorPages(options =>
    options.Conventions.AuthorizeFolder("/Coordinator", "CoordinatorOnly"));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
var rateLimitConfiguration = builder.Configuration.GetSection(AnonymousRateLimitOptions.SectionName);
var configuredRateLimits = rateLimitConfiguration.Get<AnonymousRateLimitOptions>()
    ?? new AnonymousRateLimitOptions();
if (!configuredRateLimits.IsValid())
{
    throw new InvalidOperationException(
        "AnonymousRateLimits permit limits and windows must all be positive.");
}

builder.Services.AddOptions<AnonymousRateLimitOptions>()
    .Bind(rateLimitConfiguration)
    .Validate(
        static options => options.IsValid(),
        "AnonymousRateLimits permit limits and windows must all be positive.")
    .ValidateOnStart();


builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var tier = GetAnonymousRateLimitTier(context);
        if (tier is null)
        {
            return RateLimitPartition.GetNoLimiter<string>("unlimited");
        }

        var tierOptions = tier switch
        {
            "request-mutation" => configuredRateLimits.RequestMutation,
            "private-token-read" => configuredRateLimits.PrivateTokenRead,
            "assignment-action-mutation" => configuredRateLimits.AssignmentActionMutation,
            _ => throw new InvalidOperationException($"Unknown anonymous rate-limit tier '{tier}'.")
        };
        var clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var partitionKey = $"{tier}:{clientAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = tierOptions.PermitLimit,
                Window = tierOptions.Window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = static async (context, cancellationToken) =>
    {
        var retryAfterSeconds = 1;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        context.HttpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        context.HttpContext.Response.ContentType = "text/plain";
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Try again later.",
            cancellationToken);
    };
});
builder.Services.Configure<CoordinatorOptions>(builder.Configuration.GetSection("Coordinator"));
builder.Services.AddSingleton<IAuthorizationHandler, CoordinatorAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CoordinatorOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new CoordinatorRequirement());
    });
});

var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

var oidcAuthority = builder.Configuration["Oidc:Authority"];
var oidcClientId = builder.Configuration["Oidc:ClientId"];
var oidcClientSecret = builder.Configuration["Oidc:ClientSecret"];
var hasOidcAuthority = !string.IsNullOrWhiteSpace(oidcAuthority);
var hasOidcClientId = !string.IsNullOrWhiteSpace(oidcClientId);
var hasOidcClientSecret = !string.IsNullOrWhiteSpace(oidcClientSecret);
if (hasOidcAuthority || hasOidcClientId || hasOidcClientSecret)
{
    if (!hasOidcAuthority || !hasOidcClientId || !hasOidcClientSecret)
    {
        throw new InvalidOperationException(
            "Oidc:Authority, Oidc:ClientId, and Oidc:ClientSecret must all be configured to enable OIDC.");
    }

    authentication.AddOpenIdConnect("oidc", options =>
    {
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Authority = oidcAuthority;
        options.ClientId = oidcClientId;
        options.ClientSecret = oidcClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Add("email");
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";
    });
}

builder.Services.AddHealthChecks()
    .AddCheck("postgres", new PostgresReadyHealthCheck(connectionString), tags: ["ready"]);

var app = builder.Build();

if (args.Any(argument => string.Equals(argument, "--migrate-only", StringComparison.Ordinal)))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<VolunteerCoordinatorDbContext>();
    await dbContext.Database.MigrateAsync();
    app.Logger.LogInformation("Database migration completed in migrate-only mode.");
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseForwardedHeaders();

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("DevelopmentAuth:Enabled"))
{
    app.MapGet("/development/login", DevelopmentLoginFormAsync).AllowAnonymous();
    app.MapPost("/development/login", DevelopmentLoginAsync).AllowAnonymous();
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();
app.MapRazorPages();

app.Run();

static string? GetAnonymousRateLimitTier(HttpContext context)
{
    var path = context.Request.Path.Value;
    if (HttpMethods.IsPost(context.Request.Method))
    {
        if (HasSingleRouteValue(path, "/Shifts/Request"))
        {
            return "request-mutation";
        }

        if (HasSingleRouteValue(path, "/Actions"))
        {
            return "assignment-action-mutation";
        }
    }
    else if (HttpMethods.IsGet(context.Request.Method))
    {
        if (HasSingleRouteValue(path, "/Requests/Status") ||
            HasSingleRouteValue(path, "/Actions"))
        {
            return "private-token-read";
        }
    }

    return null;
}

static bool HasSingleRouteValue(string? path, string routePrefix)
{
    if (path is null ||
        path.Length <= routePrefix.Length + 1 ||
        !path.StartsWith(routePrefix, StringComparison.OrdinalIgnoreCase) ||
        path[routePrefix.Length] != '/')
    {
        return false;
    }

    var valueStart = routePrefix.Length + 1;
    var valueEnd = path.IndexOf('/', valueStart);
    return valueEnd < 0 || (valueEnd == path.Length - 1 && valueEnd > valueStart);
}

static Task<IResult> DevelopmentLoginFormAsync(HttpContext context, IAntiforgery antiforgery)
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    var encodedToken = HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty);
    var body = $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Development coordinator login</title></head>
        <body><main><h1>Development coordinator login</h1><p>This endpoint exists only when Development authentication is explicitly enabled.</p>
        <form method="post"><input type="hidden" name="__RequestVerificationToken" value="{{encodedToken}}">
        <label>Email <input name="email" type="email" required autocomplete="email"></label><button type="submit">Sign in</button></form></main></body></html>
        """;
    return Task.FromResult(Results.Content(body, "text/html"));
}

static async Task<IResult> DevelopmentLoginAsync(
    HttpContext context,
    IAntiforgery antiforgery,
    IOptions<CoordinatorOptions> options)
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var email = form["email"].ToString().Trim();
    var normalizedEmail = email.ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(email) || !options.Value.AllowedEmails.Any(
            allowed => string.Equals(allowed.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Forbid();
    }

    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, normalizedEmail),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, normalizedEmail),
            new Claim("email_verified", bool.TrueString)
        ],
        CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    return Results.Redirect("/Coordinator/Schedule");
}

public partial class Program
{
}
