using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VolunteerCoordinator.Infrastructure.Persistence;

public sealed class VolunteerCoordinatorDbContextFactory : IDesignTimeDbContextFactory<VolunteerCoordinatorDbContext>
{
    public VolunteerCoordinatorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=volunteer_coordinator_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<VolunteerCoordinatorDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new VolunteerCoordinatorDbContext(options);
    }
}
