using Xunit;

namespace VirtualEmployee.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection :
    ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}