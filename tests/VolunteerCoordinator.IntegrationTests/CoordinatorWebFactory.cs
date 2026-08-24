using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class CoordinatorWebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public CoordinatorWebFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("DevelopmentAuth:Enabled", "true");
        builder.UseSetting("Coordinator:AllowedEmails:0", "coordinator@example.org");
    }
}
