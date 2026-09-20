using System.Security.Claims;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using VolunteerCoordinator.Domain.Assignments;
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
        Assert.Equal(HttpStatusCode.Redirect, coordinatorResponse.StatusCode);
        Assert.Equal("/Coordinator/Settings", coordinatorResponse.Headers.Location?.OriginalString);
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
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Action link verification",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                "coordinator@example.org");
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
    public async Task AllowlistedCoordinatorCancelsAssignmentWithAntiforgeryAndSeesFeedback()
    {
        await _fixture.ResetAsync();
        Guid assignmentId;
        await using (var context = _fixture.CreateContext())
        {
            var service = new VolunteerCoordinatorService(
                new EfWorkflowStore(context),
                new SystemClock(),
                new SecureTokenService(),
                new UnavailableNotificationService(context, new SystemClock()));
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Coverage cancellation",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                "coordinator@example.org");
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            await service.PublishShiftAsync(shiftId, shift.Version, "coordinator@example.org", default);
            assignmentId = (await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Coverage Volunteer",
                "coverage-volunteer@example.org",
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
        var loginToken = Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var signedIn = await client.PostAsync(
            "/development/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = loginToken,
                ["email"] = "coordinator@example.org"
            }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        var rejectedWithoutAntiforgery = await client.PostAsync(
            "/Coordinator/Coverage?handler=Cancel",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["assignmentId"] = assignmentId.ToString()
            }));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedWithoutAntiforgery.StatusCode);

        var coveragePage = await client.GetAsync("/Coordinator/Coverage");
        var coverageHtml = await coveragePage.Content.ReadAsStringAsync();
        var cancellationToken = Regex.Match(
            coverageHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(cancellationToken);

        var response = await client.PostAsync(
            "/Coordinator/Coverage?handler=Cancel",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = cancellationToken,
                ["assignmentId"] = assignmentId.ToString()
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Coordinator/Coverage", response.Headers.Location?.OriginalString);

        var redirectedPage = await client.GetAsync(response.Headers.Location);
        var redirectedHtml = await redirectedPage.Content.ReadAsStringAsync();
        Assert.Contains("Assignment cancelled. The slot is now uncovered.", redirectedHtml);
        Assert.Contains(">Uncovered<", redirectedHtml);

        var repeatedResponse = await client.PostAsync(
            "/Coordinator/Coverage?handler=Cancel",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = cancellationToken,
                ["assignmentId"] = assignmentId.ToString()
            }));
        Assert.Equal(HttpStatusCode.Redirect, repeatedResponse.StatusCode);
        var errorPage = await client.GetAsync(repeatedResponse.Headers.Location);
        Assert.Contains(
            "Only an active assignment can be cancelled.",
            await errorPage.Content.ReadAsStringAsync());

        var schedulePage = await client.GetAsync("/Coordinator/Schedule");
        var scheduleHtml = await schedulePage.Content.ReadAsStringAsync();
        Assert.Contains(
            "Deactivation supersedes pending requests, cancels active assignments, and invalidates their action links.",
            scheduleHtml);
        Assert.Contains("Deactivate and resolve", scheduleHtml);

        await using var verificationContext = _fixture.CreateContext();
        Assert.Equal(
            AssignmentStatus.Cancelled,
            (await verificationContext.Assignments.SingleAsync(x => x.Id == assignmentId)).Status);
        Assert.Contains(
            await verificationContext.AuditEntries.ToListAsync(),
            x => x.Action == "AssignmentCancelledByCoordinator" &&
                 x.Actor == "COORDINATOR@EXAMPLE.ORG" &&
                 x.EntityId == assignmentId);
    }

    [Fact]
    public async Task AnonymousAndNonAllowlistedCallersCannotCancelAssignments()
    {
        await _fixture.ResetAsync();
        Guid assignmentId;
        await using (var context = _fixture.CreateContext())
        {
            var service = new VolunteerCoordinatorService(
                new EfWorkflowStore(context),
                new SystemClock(),
                new SecureTokenService(),
                new UnavailableNotificationService(context, new SystemClock()));
            var starts = DateTimeOffset.UtcNow.AddDays(2);
            var shiftId = await ScheduleTestHelpers.CreateShiftFromInstantsAsync(
                service,
                "Protected cancellation",
                null,
                null,
                starts,
                starts.AddHours(1),
                0,
                "coordinator@example.org");
            var shift = (await service.ListShiftsAsync(default)).Single(x => x.Id == shiftId);
            assignmentId = (await service.AssignDirectlyAsync(
                shift.Slots.Single().Id,
                "Protected Volunteer",
                "protected-volunteer@example.org",
                null,
                "coordinator@example.org",
                default)).AssignmentId;
        }

        using (var factory = new CoordinatorWebFactory(_fixture.ConnectionString))
        using (var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        }))
        {
            var anonymousResponse = await anonymousClient.PostAsync(
                "/Coordinator/Coverage?handler=Cancel",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["assignmentId"] = assignmentId.ToString()
                }));
            Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
            Assert.Equal("/Account/Login", anonymousResponse.Headers.Location?.AbsolutePath);
        }

        using (var factory = new CoordinatorWebFactory(
                   _fixture.ConnectionString,
                   authenticateNonCoordinator: true))
        using (var nonCoordinatorClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        }))
        {
            var forbiddenResponse = await nonCoordinatorClient.PostAsync(
                "/Coordinator/Coverage?handler=Cancel",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["assignmentId"] = assignmentId.ToString()
                }));
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        }

        await using var verificationContext = _fixture.CreateContext();
        Assert.Equal(
            AssignmentStatus.Assigned,
            (await verificationContext.Assignments.SingleAsync(x => x.Id == assignmentId)).Status);
        Assert.DoesNotContain(
            await verificationContext.AuditEntries.ToListAsync(),
            x => x.Action == "AssignmentCancelledByCoordinator" && x.EntityId == assignmentId);
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
    public async Task TwoAllowlistedCoordinatorsCanSignInIndependently()
    {
        await _fixture.ResetAsync();
        using var factory = new CoordinatorWebFactory(
            _fixture.ConnectionString,
            allowedEmails:
            [
                "incoming@example.org",
                "outgoing@example.org"
            ]);

        using var incomingClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var incomingLogin = await SignInAsync(incomingClient, "incoming@example.org");
        Assert.Equal(HttpStatusCode.Redirect, incomingLogin.StatusCode);
        var incomingSchedule = await incomingClient.GetAsync("/Coordinator/Schedule");
        Assert.Equal(HttpStatusCode.Redirect, incomingSchedule.StatusCode);
        Assert.Equal("/Coordinator/Settings", incomingSchedule.Headers.Location?.OriginalString);

        using var outgoingClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var outgoingLogin = await SignInAsync(outgoingClient, "outgoing@example.org");
        Assert.Equal(HttpStatusCode.Redirect, outgoingLogin.StatusCode);
        Assert.Equal("/Coordinator/Schedule", outgoingLogin.Headers.Location?.OriginalString);
        var outgoingSchedule = await outgoingClient.GetAsync("/Coordinator/Schedule");
        Assert.Equal(HttpStatusCode.Redirect, outgoingSchedule.StatusCode);
        Assert.Equal("/Coordinator/Settings", outgoingSchedule.Headers.Location?.OriginalString);

        using var thirdClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var thirdLogin = await SignInAsync(thirdClient, "third@example.org");
        Assert.Equal(HttpStatusCode.Redirect, thirdLogin.StatusCode);
        Assert.Contains("/Account/AccessDenied", thirdLogin.Headers.Location?.OriginalString ?? string.Empty);
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email)
    {
        var loginForm = await client.GetAsync("/development/login");
        var loginHtml = await loginForm.Content.ReadAsStringAsync();
        var loginToken = Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(loginToken);

        return await client.PostAsync(
            "/development/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = loginToken,
                ["email"] = email
            }));
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
