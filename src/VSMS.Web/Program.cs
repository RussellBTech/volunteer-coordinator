using System.Security.Claims;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Resend;
using Serilog;
using VSMS.Core.Interfaces;
using VSMS.Infrastructure.Data;
using VSMS.Infrastructure.Services;
using VSMS.Jobs;
using VSMS.Web.Authorization;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/vsms-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("Starting VSMS application");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

// Add services - Use SQLite for development if no PostgreSQL connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    // Use SQLite for local development
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "vsms.db");
    builder.Services.AddDbContext<VsmsDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));
}
else
{
    builder.Services.AddDbContext<VsmsDbContext>(options =>
        options.UseNpgsql(connectionString));
}

// Authentication - Google and GitHub OAuth
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
var hasGoogleAuth = !string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret);

var githubClientId = builder.Configuration["GitHub:ClientId"];
var githubClientSecret = builder.Configuration["GitHub:ClientSecret"];
var hasGitHubAuth = !string.IsNullOrEmpty(githubClientId) && !string.IsNullOrEmpty(githubClientSecret);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // No default challenge - let login page choose provider
})
.AddCookie(options =>
{
    options.LoginPath = "/admin/login";
    options.LogoutPath = "/admin/logout";
    options.AccessDeniedPath = "/admin/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

if (hasGoogleAuth)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.SaveTokens = true;
    });
    Log.Information("Google OAuth configured");
}

if (hasGitHubAuth)
{
    authBuilder.AddOAuth("GitHub", options =>
    {
        options.ClientId = githubClientId!;
        options.ClientSecret = githubClientSecret!;
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.CallbackPath = "/signin-github";
        options.SaveTokens = true;
        options.Scope.Add("user:email");

        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
        options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
        options.ClaimActions.MapJsonKey("urn:github:login", "login");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.UserAgent.ParseAdd("VSMS-App");

                var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                var user = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                context.RunClaimActions(user);

                // If email is null, fetch from emails endpoint
                if (string.IsNullOrEmpty(context.Identity?.FindFirst(ClaimTypes.Email)?.Value))
                {
                    var emailRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
                    emailRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    emailRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                    emailRequest.Headers.UserAgent.ParseAdd("VSMS-App");

                    var emailResponse = await context.Backchannel.SendAsync(emailRequest, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                    if (emailResponse.IsSuccessStatusCode)
                    {
                        var emails = await emailResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                        foreach (var email in emails.EnumerateArray())
                        {
                            if (email.GetProperty("primary").GetBoolean())
                            {
                                context.Identity?.AddClaim(new Claim(ClaimTypes.Email, email.GetProperty("email").GetString() ?? ""));
                                break;
                            }
                        }
                    }
                }
            }
        };
    });
    Log.Information("GitHub OAuth configured");
}

if (!hasGoogleAuth && !hasGitHubAuth)
{
    Log.Warning("No OAuth providers configured - admin authentication will not work");
}

// Authorization
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.Requirements.Add(new AdminRequirement()));
});

// Application Services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailService, ResendEmailService>();
builder.Services.AddScoped<ICalendarService, GoogleCalendarService>();

// Resend Email Client
builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
{
    o.ApiToken = builder.Configuration["Email:ApiKey"] ?? "";
});
builder.Services.AddTransient<IResend, ResendClient>();

// Background Jobs
builder.Services.AddScoped<ShiftReminderJobs>();
builder.Services.AddScoped<MonthPublishJob>();

if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

    builder.Services.AddHangfireServer();
}

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
    options.Conventions.AllowAnonymousToPage("/Admin/LoginCallback");
    options.Conventions.AllowAnonymousToPage("/Admin/Logout");
    options.Conventions.AllowAnonymousToPage("/Admin/AccessDenied");
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    // Development-only seed endpoint
    app.MapGet("/dev/seed", async (VsmsDbContext db) =>
    {
        if (!db.Volunteers.Any())
        {
            db.Volunteers.AddRange(
                new VSMS.Core.Entities.Volunteer { Name = "John Smith", Email = "john@test.com", Phone = "555-0101", IsActive = true },
                new VSMS.Core.Entities.Volunteer { Name = "Jane Doe", Email = "jane@test.com", Phone = "555-0102", IsActive = true },
                new VSMS.Core.Entities.Volunteer { Name = "Bob Wilson", Email = "bob@test.com", Phone = "555-0103", IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        if (!db.Shifts.Any())
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            db.Shifts.AddRange(
                new VSMS.Core.Entities.Shift { Date = today.AddDays(1), TimeSlotId = 1, Role = VSMS.Core.Enums.ShiftRole.InPerson, VolunteerId = 1, Backup1VolunteerId = 2, Status = VSMS.Core.Enums.ShiftStatus.Assigned, AssignedAt = DateTime.UtcNow },
                new VSMS.Core.Entities.Shift { Date = today.AddDays(1), TimeSlotId = 2, Role = VSMS.Core.Enums.ShiftRole.Phone, Status = VSMS.Core.Enums.ShiftStatus.Open },
                new VSMS.Core.Entities.Shift { Date = today.AddDays(2), TimeSlotId = 1, Role = VSMS.Core.Enums.ShiftRole.InPerson, VolunteerId = 2, Status = VSMS.Core.Enums.ShiftStatus.Assigned, AssignedAt = DateTime.UtcNow },
                new VSMS.Core.Entities.Shift { Date = today.AddDays(2), TimeSlotId = 2, Role = VSMS.Core.Enums.ShiftRole.InPerson, Status = VSMS.Core.Enums.ShiftStatus.Open },
                new VSMS.Core.Entities.Shift { Date = today.AddDays(3), TimeSlotId = 1, Role = VSMS.Core.Enums.ShiftRole.Phone, VolunteerId = 3, Backup1VolunteerId = 1, Status = VSMS.Core.Enums.ShiftStatus.Confirmed, AssignedAt = DateTime.UtcNow, ConfirmedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        if (!db.AdminUsers.Any())
        {
            db.AdminUsers.Add(new VSMS.Core.Entities.AdminUser { Email = "admin@test.com", GoogleId = "dev-test", Name = "Test Admin" });
            await db.SaveChangesAsync();
        }

        return Results.Ok("Test data seeded!");
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Hangfire Dashboard (protected by admin auth)
if (!string.IsNullOrEmpty(connectionString))
{
    app.MapHangfireDashboard("/admin/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() }
    }).RequireAuthorization("AdminOnly");

    // Register recurring jobs
    RecurringJob.AddOrUpdate<ShiftReminderJobs>(
        "seven-day-reminders",
        x => x.SendSevenDayReminders(),
        "0 9 * * *"); // 9am daily

    RecurringJob.AddOrUpdate<ShiftReminderJobs>(
        "24-hour-reminders",
        x => x.Send24HourReminders(),
        "0 * * * *"); // Every hour

    RecurringJob.AddOrUpdate<ShiftReminderJobs>(
        "auto-reopen-shifts",
        x => x.AutoReopenUnconfirmedShifts(),
        "0 * * * *"); // Every hour
}

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.MapGet("/", () => Results.Redirect("/shifts/open"));

app.MapRazorPages();

// Auto-apply migrations and seed data
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<VsmsDbContext>();

    // Use EnsureCreated for SQLite (local dev), MigrateAsync for PostgreSQL (production)
    if (string.IsNullOrEmpty(connectionString))
    {
        Log.Information("Using SQLite - creating database if needed");
        await db.Database.EnsureCreatedAsync();
    }
    else if (app.Environment.IsDevelopment())
    {
        await db.Database.MigrateAsync();
    }

    // Seed initial admin if configured and not exists
    var seedAdminEmail = builder.Configuration["App:SeedAdminEmail"];
    if (!string.IsNullOrEmpty(seedAdminEmail) && !db.AdminUsers.Any())
    {
        db.AdminUsers.Add(new VSMS.Core.Entities.AdminUser
        {
            Email = seedAdminEmail,
            GoogleId = "seed-pending",
            Name = "Admin"
        });
        await db.SaveChangesAsync();
        Log.Information("Seeded admin user: {Email}", seedAdminEmail);
    }
}

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }

// Hangfire authorization filter
public class HangfireAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        // In production, this is protected by ASP.NET Core authorization
        // The RequireAuthorization("AdminOnly") on the dashboard handles the actual auth
        return true;
    }
}
