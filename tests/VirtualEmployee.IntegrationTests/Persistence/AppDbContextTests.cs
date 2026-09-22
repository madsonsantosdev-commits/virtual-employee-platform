using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Persistence;

[Collection(PostgreSqlCollection.Name)]
public sealed class AppDbContextTests
{
    private readonly PostgreSqlFixture _fixture;

    public AppDbContextTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ShouldPersistAndRetrieveTenant()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        // Tenant é a raiz do isolamento e não exige
        // um TenantContext inicializado.
        var tenantContext = new TenantContext();

        await using var dbContext = new AppDbContext(
            options,
            tenantContext);

        var tenant = new Tenant(
            Guid.NewGuid(),
            "Integration Test Tenant");

        dbContext.Tenants.Add(tenant);

        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();

        var persistedTenant = await dbContext.Tenants
            .SingleAsync(x => x.Id == tenant.Id);

        Assert.Equal(
            tenant.Id,
            persistedTenant.Id);

        Assert.Equal(
            "Integration Test Tenant",
            persistedTenant.Name);

        dbContext.Tenants.Remove(persistedTenant);

        await dbContext.SaveChangesAsync();
    }
}