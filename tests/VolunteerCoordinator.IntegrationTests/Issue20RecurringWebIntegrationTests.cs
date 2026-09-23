using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class Issue20RecurringWebIntegrationTests
{
    private const string Coordinator = "coordinator@example.org";
    private readonly PostgreSqlFixture _fixture;

    public Issue20RecurringWebIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RecurringCreatePreviewUsesLocalFieldsAndShowsConcreteRoles()
    {
        await _fixture.ResetAsync();
        var clock = new ScheduleTestHelpers.FixedClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = new CoordinatorWebFactory(_fixture.ConnectionString, clock: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client);

        var settingsPage = await client.GetAsync("/Coordinator/Settings");
        var settingsToken = ExtractAntiforgeryToken(await settingsPage.Content.ReadAsStringAsync());
        var configured = await client.PostAsync(
            "/Coordinator/Settings",
            Form(
                ("__RequestVerificationToken", settingsToken),
                ("TimeZoneId", "Etc/UTC"),
                ("ExpectedVersion", "")));
        Assert.Equal(HttpStatusCode.Redirect, configured.StatusCode);

        await using var context = _fixture.CreateContext();
        var settingsVersion = await context.GroupSettings.Select(x => x.Version).SingleAsync();
        var createPage = await client.GetAsync("/Coordinator/Recurring/Create");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("Preview dates", createHtml);
        Assert.Contains("Elapsed duration in minutes", createHtml);
        Assert.DoesNotContain("RRULE", createHtml, StringComparison.OrdinalIgnoreCase);
        var values = new (string Key, string Value)[]
        {
            ("__RequestVerificationToken", ExtractAntiforgeryToken(createHtml)),
            ("Title", "Weekly local meeting"),
            ("Location", "Hall"),
            ("VolunteerInstructions", "Use the side door."),
            ("InternalCoordinatorNotes", "Private note"),
            ("RecurrenceKind", "Weekly"),
            ("Interval", "1"),
            ("Weekdays", "Monday"),
            ("Weekdays", "Thursday"),
            ("AnchorLocalDate", "2026-01-05"),
            ("LocalStartTime", "09:00"),
            ("DurationMinutes", "120"),
            ("BackupSlotCount", "1"),
            ("HorizonWeeks", "4"),
            ("AmbiguousTimeChoice", "FirstOccurrence"),
            ("ExpectedSettingsVersion", settingsVersion.ToString())
        };
        var preview = await client.PostAsync(ExtractButtonAction(createHtml, "Preview dates"), Form(values));
        var previewHtml = await preview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Preview of actual dates", previewHtml);
        Assert.Contains("Primary", previewHtml);
        Assert.Contains("Backup 1", previewHtml);
        Assert.Contains("Monday", previewHtml);
        Assert.Contains("Thursday", previewHtml);
        var created = await client.PostAsync(ExtractButtonAction(createHtml, "Save recurring schedule"), Form(values));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var seriesId = await context.RecurringShiftSeries.Select(x => x.Id).SingleAsync();
        var publishHtml = await client.GetStringAsync($"/Coordinator/Recurring/Publish/{seriesId}");
        var publicationPreview = await client.PostAsync(
            ExtractButtonAction(publishHtml, "Review selected dates"),
            Form(
                ("__RequestVerificationToken", ExtractAntiforgeryToken(publishHtml)),
                ("FromLocalDate", "2026-01-05"),
                ("ThroughLocalDate", "2026-02-02"),
                ("ExpectedSeriesVersion", ExtractHiddenValue(publishHtml, "ExpectedSeriesVersion")),
                ("ExpectedVersions", "")));
        var publicationHtml = await publicationPreview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, publicationPreview.StatusCode);
        Assert.Contains("All selected concrete shifts are current and eligible.", publicationHtml);
        var reviewedVersions = ExtractHiddenValue(publicationHtml, "ExpectedVersions");
        Assert.NotEmpty(reviewedVersions);

        var confirmed = await client.PostAsync(
            ExtractButtonAction(publicationHtml, "Publish all selected dates"),
            Form(
                ("__RequestVerificationToken", ExtractAntiforgeryToken(publicationHtml)),
                ("FromLocalDate", "2026-01-05"),
                ("ThroughLocalDate", "2026-02-02"),
                ("ExpectedSeriesVersion", ExtractHiddenValue(publicationHtml, "ExpectedSeriesVersion")),
                ("ExpectedVersions", reviewedVersions)));
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        var detail = await client.GetStringAsync(confirmed.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Published (0)", detail);
        Assert.Contains("Published (", detail);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    private static string ExtractButtonAction(string html, string label)
    {
        var match = Regex.Match(html, $"<button[^>]*formaction=\"([^\"]+)\"[^>]*>{Regex.Escape(label)}</button>");
        Assert.True(match.Success, $"The {label} button did not target its server handler.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ExtractHiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"id=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, $"The {name} field was missing.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return match.Groups[1].Value;
    }

    private static async Task SignInAsync(HttpClient client)
    {
        var login = await client.GetAsync("/development/login");
        var token = ExtractAntiforgeryToken(await login.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/development/login",
            Form(
                ("__RequestVerificationToken", token),
                ("email", Coordinator)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
