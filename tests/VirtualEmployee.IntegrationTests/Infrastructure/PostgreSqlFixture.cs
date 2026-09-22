using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Infrastructure;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public string ConnectionString { get; }

    public PostgreSqlFixture()
    {
        ConnectionString =
            Environment.GetEnvironmentVariable(
                "TEST_DATABASE_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Environment variable 'TEST_DATABASE_CONNECTION_STRING' was not configured.");
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var dbContext = new AppDbContext(
            options,
            new TenantContext());

        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}