using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using VolunteerCoordinator.Infrastructure.Persistence;
using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("volunteer_coordinator_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public VolunteerCoordinatorDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<VolunteerCoordinatorDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new VolunteerCoordinatorDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "ActionTokens", "Assignments", "ShiftRequests", "ShiftSlots", "Shifts", "Volunteers", "AuditEntries", "NotificationAttempts" CASCADE""");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
