using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;

namespace VirtualEmployee.IntegrationTests.Tenancy;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task Businesses_ShouldBeIsolatedByTenant()
    {
        var connectionString = GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var tenantA = new Tenant(
            Guid.NewGuid(),
            "Tenant A");

        var tenantB = new Tenant(
            Guid.NewGuid(),
            "Tenant B");

        var businessA = new Business(
            Guid.NewGuid(),
            tenantA.Id,
            "Business A");

        var businessB = new Business(
            Guid.NewGuid(),
            tenantB.Id,
            "Business B");

        var tenantContextA = CreateTenantContext(tenantA.Id);
        var tenantContextB = CreateTenantContext(tenantB.Id);

        // Cria os tenants e o Business A usando Tenant A.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            await dbContext.Database.MigrateAsync();

            dbContext.Tenants.AddRange(
                tenantA,
                tenantB);

            dbContext.Businesses.Add(businessA);

            await dbContext.SaveChangesAsync();
        }

        // Business B é criado usando Tenant B.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            dbContext.Businesses.Add(businessB);

            await dbContext.SaveChangesAsync();
        }

        // Tenant A só pode enxergar Business A.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            var businesses = await dbContext.Businesses
                .ToListAsync();

            Assert.Single(businesses);

            Assert.Equal(
                businessA.Id,
                businesses[0].Id);

            Assert.Equal(
                tenantA.Id,
                businesses[0].TenantId);
        }

        // Tenant B só pode enxergar Business B.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            var businesses = await dbContext.Businesses
                .ToListAsync();

            Assert.Single(businesses);

            Assert.Equal(
                businessB.Id,
                businesses[0].Id);

            Assert.Equal(
                tenantB.Id,
                businesses[0].TenantId);
        }

        // Remove Business A dentro do Tenant A.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            var business = await dbContext.Businesses
                .SingleAsync(x => x.Id == businessA.Id);

            dbContext.Businesses.Remove(business);

            await dbContext.SaveChangesAsync();
        }

        // Remove Business B dentro do Tenant B.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            var business = await dbContext.Businesses
                .SingleAsync(x => x.Id == businessB.Id);

            dbContext.Businesses.Remove(business);

            await dbContext.SaveChangesAsync();
        }

        // Tenant é a raiz do isolamento e não é ITenantScoped.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            var tenants = await dbContext.Tenants
                .Where(x =>
                    x.Id == tenantA.Id ||
                    x.Id == tenantB.Id)
                .ToListAsync();

            dbContext.Tenants.RemoveRange(tenants);

            await dbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_WithBusinessFromAnotherTenant_ShouldThrow()
    {
        var connectionString = GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var tenantContextA = CreateTenantContext(tenantAId);

        await using var dbContext = new AppDbContext(
            options,
            tenantContextA);

        var businessB = new Business(
            Guid.NewGuid(),
            tenantBId,
            "Business B");

        dbContext.Businesses.Add(businessB);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task SaveChanges_WithModifiedBusinessFromAnotherTenant_ShouldThrow()
    {
        var connectionString = GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var tenantContextA = CreateTenantContext(tenantAId);

        await using var dbContext = new AppDbContext(
            options,
            tenantContextA);

        var businessB = new Business(
            Guid.NewGuid(),
            tenantBId,
            "Business B");

        dbContext.Attach(businessB);

        dbContext.Entry(businessB).State =
            EntityState.Modified;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task SaveChanges_WithDeletedBusinessFromAnotherTenant_ShouldThrow()
    {
        var connectionString = GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var tenantContextA = CreateTenantContext(tenantAId);

        await using var dbContext = new AppDbContext(
            options,
            tenantContextA);

        var businessB = new Business(
            Guid.NewGuid(),
            tenantBId,
            "Business B");

        dbContext.Attach(businessB);

        dbContext.Entry(businessB).State =
            EntityState.Deleted;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable(
                   "TEST_DATABASE_CONNECTION_STRING")
               ?? throw new InvalidOperationException(
                   "Environment variable 'TEST_DATABASE_CONNECTION_STRING' was not configured.");
    }

    private static TenantContext CreateTenantContext(Guid tenantId)
    {
        var context = new TenantContext();
        context.Initialize(tenantId);

        return context;
    }
}