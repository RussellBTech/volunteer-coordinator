using Xunit;

namespace VolunteerCoordinator.IntegrationTests;

[CollectionDefinition("PostgreSQL")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
}
