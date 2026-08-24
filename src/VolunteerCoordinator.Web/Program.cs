using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<VolunteerCoordinatorDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
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
