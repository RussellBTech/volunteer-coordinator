using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public VolunteerCoordinatorDbContext CreateContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<VolunteerCoordinatorDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString)
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
            """TRUNCATE TABLE "RecurringShiftOccurrences", "RecurringShiftSeriesRevisions", "RecurringShiftSeries", "ResendWebhookReceipts", "NotificationDeliveryAttempts", "NotificationIntents", "RecoveryTokens", "VolunteerAccessCapabilities", "ActionTokens", "Assignments", "ShiftRequests", "ShiftSlots", "Shifts", "GroupSettings", "Volunteers", "AuditEntries", "NotificationAttempts" CASCADE""");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var databaseName = $"vc_process_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = "postgres"
        };
        await using var connection = new NpgsqlConnection(adminConnectionString.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();

        var databaseConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName
        };
        return databaseConnectionString.ConnectionString;
    }

    public async Task DropDatabaseAsync(string connectionString)
    {
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        var adminConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = "postgres"
        };
        await using var connection = new NpgsqlConnection(adminConnectionString.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
