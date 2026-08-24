using System.Security.Claims;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using VolunteerCoordinator.Web.Security;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using VolunteerCoordinator.Infrastructure.Time;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AuthorizationIntegrationTests
{
    private readonly PostgreSqlFixture _fixture;

    public AuthorizationIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublicPagesAreAnonymousAndCoordinatorPagesRequireAllowlistedIdentity()
    {
        await _fixture.ResetAsync();
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var publicResponse = await client.GetAsync("/Shifts");
        var coordinatorResponse = await client.GetAsync("/Coordinator/Schedule");

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, coordinatorResponse.StatusCode);
        Assert.Equal("/Account/Login", coordinatorResponse.Headers.Location?.AbsolutePath);

        var loginForm = await client.GetAsync("/development/login");
        var html = await loginForm.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var denied = await client.PostAsync("/development/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = "not-allowed@example.org"
        }));
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        loginForm = await client.GetAsync("/development/login");
        html = await loginForm.Content.ReadAsStringAsync();
        token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var signedIn = await client.PostAsync("/development/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = "Coordinator@Example.org"
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        coordinatorResponse = await client.GetAsync("/Coordinator/Schedule");
        Assert.Equal(HttpStatusCode.OK, coordinatorResponse.StatusCode);
    }

    [Fact]
    public async Task GeneratedActionLinksAreDisplayedInThePostResponse()
    {
        await _fixture.ResetAsync();
        Guid assignmentId;
        await using (var context = _fixture.CreateContext())
        {
            var clock = new SystemClock();
            var service = new VolunteerCoordinatorService(
                new EfWorkflowStore(context),
                clock,
                new SecureTokenService(),
                new UnavailableNotificationService(context, clock));
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            var shiftId = await service.CreateShiftAsync(
                "Action link verification",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                "coordinator@example.org",
                default);
            var slot = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId).Slots.Single();
            assignmentId = (await service.AssignDirectlyAsync(
                slot.Id,
                "Verification Volunteer",
                "volunteer@example.org",
                null,
                "coordinator@example.org",
                default)).AssignmentId;
        }

        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var loginForm = await client.GetAsync("/development/login");
        var loginHtml = await loginForm.Content.ReadAsStringAsync();
        var loginToken = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var signedIn = await client.PostAsync("/development/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken,
            ["email"] = "coordinator@example.org"
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        var path = $"/Coordinator/Assignments/Links/{assignmentId}";
        var linkForm = await client.GetAsync(path);
        var linkFormHtml = await linkForm.Content.ReadAsStringAsync();
        var linkToken = Regex.Match(linkFormHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(linkToken);

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = linkToken
        }));
        var responseHtml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Copy these links now", responseHtml);
        Assert.Contains("<strong>Confirm</strong>", responseHtml);
        Assert.Contains("<strong>Decline</strong>", responseHtml);
        Assert.Contains("<strong>Cancel</strong>", responseHtml);
        Assert.Contains("/Actions/", responseHtml);
    }

    [Fact]
    public async Task AuthenticatedVerifiedNonCoordinatorIsForbidden()
    {
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            authenticateNonCoordinator: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/Coordinator/Schedule");
        var publicResponse = await client.GetAsync("/Shifts");
        var publicHtml = await publicResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        Assert.DoesNotContain("/Coordinator/", publicHtml);
        Assert.Contains("Sign out", publicHtml);
    }

    [Fact]
    public void CoordinatorIdentityRequiresAVerifiedEmailClaim()
    {
        var usernameOnly = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("preferred_username", "coordinator@example.org"),
                new Claim("email_verified", bool.TrueString)
            ]));
        var unverifiedEmail = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Email, "coordinator@example.org"),
                new Claim("email_verified", bool.FalseString)
            ]));
        var verifiedEmail = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("email", "Coordinator@Example.org"),
                new Claim("email_verified", bool.TrueString)
            ]));

        Assert.Null(CoordinatorIdentity.GetEmail(usernameOnly));
        Assert.Null(CoordinatorIdentity.GetEmail(unverifiedEmail));
        Assert.Equal("COORDINATOR@EXAMPLE.ORG", CoordinatorIdentity.GetEmail(verifiedEmail));
    }
}
