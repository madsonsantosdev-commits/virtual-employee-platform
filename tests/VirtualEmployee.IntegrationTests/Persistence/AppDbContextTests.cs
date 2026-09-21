using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;

namespace VirtualEmployee.IntegrationTests.Persistence;

public sealed class AppDbContextTests
{
    [Fact]
    public async Task ShouldPersistAndRetrieveTenant()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Environment variable 'TEST_DATABASE_CONNECTION_STRING' was not configured.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.Initialize(Guid.NewGuid());

        await using var dbContext = new AppDbContext(
            options,
            tenantContext);

        await dbContext.Database.MigrateAsync();

        var tenant = new Tenant(
            Guid.NewGuid(),
            "Integration Test Tenant");

        dbContext.Tenants.Add(tenant);

        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();

        var persistedTenant = await dbContext.Tenants
            .SingleAsync(x => x.Id == tenant.Id);

        Assert.Equal(tenant.Id, persistedTenant.Id);
        Assert.Equal("Integration Test Tenant", persistedTenant.Name);

        dbContext.Tenants.Remove(persistedTenant);
        await dbContext.SaveChangesAsync();
    }
}
