using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

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
}
