using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
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

// Authentication - Google OAuth only if credentials are configured
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
var hasGoogleAuth = !string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    if (hasGoogleAuth)
    {
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    }
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
}
else
{
    Log.Warning("Google OAuth not configured - admin authentication will not work");
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
