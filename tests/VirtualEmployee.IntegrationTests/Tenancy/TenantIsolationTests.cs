using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Tenancy;

[Collection(PostgreSqlCollection.Name)]
public sealed class TenantIsolationTests
{
    private static readonly DateTimeOffset BusinessCreatedAt =
        new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LocationCreatedAt =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public TenantIsolationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    // =========================================================
    // Business
    // =========================================================

    [Fact]
    public async Task Businesses_ShouldBeIsolatedByTenant()
    {
        var options = CreateOptions();

        var tenantA = new Tenant(
            Guid.NewGuid(),
            "Tenant A");

        var tenantB = new Tenant(
            Guid.NewGuid(),
            "Tenant B");

        var businessA = CreateBusiness(
            Guid.NewGuid(),
            tenantA.Id,
            "Business A");

        var businessB = CreateBusiness(
            Guid.NewGuid(),
            tenantB.Id,
            "Business B");

        var tenantContextA = CreateTenantContext(tenantA.Id);
        var tenantContextB = CreateTenantContext(tenantB.Id);

        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            dbContext.Tenants.AddRange(
                tenantA,
                tenantB);

            dbContext.Businesses.Add(businessA);

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            dbContext.Businesses.Add(businessB);

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            var businesses = await dbContext.Businesses
                .ToListAsync();

            var business = Assert.Single(businesses);

            Assert.Equal(
                businessA.Id,
                business.Id);

            Assert.Equal(
                tenantA.Id,
                business.TenantId);
        }

        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            var businesses = await dbContext.Businesses
                .ToListAsync();

            var business = Assert.Single(businesses);

            Assert.Equal(
                businessB.Id,
                business.Id);

            Assert.Equal(
                tenantB.Id,
                business.TenantId);
        }

        await RemoveBusinessAsync(
            options,
            tenantA.Id,
            businessA.Id);

        await RemoveBusinessAsync(
            options,
            tenantB.Id,
            businessB.Id);

        await RemoveTenantsAsync(
            options,
            tenantA.Id,
            tenantB.Id);
    }

    [Fact]
    public async Task SaveChanges_WithBusinessFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId));

        var businessB = CreateBusiness(
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
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId));

        var businessB = CreateBusiness(
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
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId));

        var businessB = CreateBusiness(
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

    [Fact]
    public async Task BusinessById_FromAnotherTenant_ShouldNotBeFound()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await setupContext.SaveChangesAsync();
        }

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var business = await tenantAContext.Businesses
                .SingleOrDefaultAsync(
                    x => x.Id == businessBId);

            Assert.Null(business);
        }

        await RemoveBusinessAsync(
            options,
            tenantBId,
            businessBId);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }

    [Fact]
    public async Task Update_WithForgedTenantId_ShouldNotModifyAnotherTenantBusiness()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await setupContext.SaveChangesAsync();
        }

        // Tenant A knows the Id of Business B and forges TenantId
        // using its own tenant.
        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var forgedBusiness = CreateBusiness(
                businessBId,
                tenantAId,
                "Forged Business");

            tenantAContext.Attach(forgedBusiness);

            tenantAContext.Entry(forgedBusiness).State =
                EntityState.Modified;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => tenantAContext.SaveChangesAsync());
        }

        // Verify the physical state without relying on the query filter.
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var business = await verificationContext.Businesses
                .IgnoreQueryFilters()
                .SingleAsync(
                    x => x.Id == businessBId);

            Assert.Equal(
                "Business B",
                business.Name);

            Assert.Equal(
                tenantBId,
                business.TenantId);
        }

        await RemoveBusinessAsync(
            options,
            tenantBId,
            businessBId);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }

    [Fact]
    public async Task Delete_WithForgedTenantId_ShouldNotDeleteAnotherTenantBusiness()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await setupContext.SaveChangesAsync();
        }

        // Tenant A attempts to delete Business B using a detached
        // entity with a forged TenantId.
        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var forgedBusiness = CreateBusiness(
                businessBId,
                tenantAId,
                "Forged Business");

            tenantAContext.Attach(forgedBusiness);

            tenantAContext.Entry(forgedBusiness).State =
                EntityState.Deleted;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => tenantAContext.SaveChangesAsync());
        }

        // Verify the physical state without relying on the query filter.
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var business = await verificationContext.Businesses
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.Id == businessBId);

            Assert.NotNull(business);

            Assert.Equal(
                tenantBId,
                business.TenantId);

            Assert.Equal(
                "Business B",
                business.Name);
        }

        await RemoveBusinessAsync(
            options,
            tenantBId,
            businessBId);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }

    // =========================================================
    // Location
    // =========================================================

    [Fact]
    public async Task Locations_ShouldBeIsolatedByTenant()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            tenantAContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            tenantAContext.Businesses.Add(
                CreateBusiness(
                    businessAId,
                    tenantAId,
                    "Business A"));

            await tenantAContext.SaveChangesAsync();
        }

        await using (var tenantBContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            tenantBContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await tenantBContext.SaveChangesAsync();
        }

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            tenantAContext.Locations.Add(
                CreateLocation(
                    locationAId,
                    tenantAId,
                    businessAId,
                    "Location A"));

            await tenantAContext.SaveChangesAsync();
        }

        await using (var tenantBContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            tenantBContext.Locations.Add(
                CreateLocation(
                    locationBId,
                    tenantBId,
                    businessBId,
                    "Location B"));

            await tenantBContext.SaveChangesAsync();
        }

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var locations = await tenantAContext.Locations
                .Where(x =>
                    x.Id == locationAId ||
                    x.Id == locationBId)
                .ToListAsync();

            var location = Assert.Single(locations);

            Assert.Equal(
                locationAId,
                location.Id);

            Assert.Equal(
                tenantAId,
                location.TenantId);
        }

        await using (var tenantBContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var locations = await tenantBContext.Locations
                .Where(x =>
                    x.Id == locationAId ||
                    x.Id == locationBId)
                .ToListAsync();

            var location = Assert.Single(locations);

            Assert.Equal(
                locationBId,
                location.Id);

            Assert.Equal(
                tenantBId,
                location.TenantId);
        }

        await RemoveLocationAsync(
            options,
            tenantAId,
            locationAId);

        await RemoveLocationAsync(
            options,
            tenantBId,
            locationBId);

        await RemoveBusinessAsync(
            options,
            tenantAId,
            businessAId);

        await RemoveBusinessAsync(
            options,
            tenantBId,
            businessBId);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }

    [Fact]
    public async Task SaveChanges_WithLocationFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId));

        var locationB = CreateLocation(
            Guid.NewGuid(),
            tenantBId,
            Guid.NewGuid(),
            "Location B");

        dbContext.Locations.Add(locationB);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task Update_WithForgedTenantId_ShouldNotModifyAnotherTenantLocation()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessBId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            setupContext.Locations.Add(
                CreateLocation(
                    locationBId,
                    tenantBId,
                    businessBId,
                    "Location B"));

            await setupContext.SaveChangesAsync();
        }

        // Tenant A knows Location B's Id and forges TenantId
        // using its own tenant.
        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var forgedLocation = CreateLocation(
                locationBId,
                tenantAId,
                businessBId,
                "Forged Location");

            tenantAContext.Attach(forgedLocation);

            tenantAContext.Entry(forgedLocation).State =
                EntityState.Modified;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => tenantAContext.SaveChangesAsync());
        }

        // Verify the physical state without relying on the query filter.
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var location = await verificationContext.Locations
                .IgnoreQueryFilters()
                .SingleAsync(
                    x => x.Id == locationBId);

            Assert.Equal(
                locationBId,
                location.Id);

            Assert.Equal(
                tenantBId,
                location.TenantId);

            Assert.Equal(
                "Location B",
                location.Name);
        }

        await RemoveLocationAsync(
            options,
            tenantBId,
            locationBId);

        await RemoveBusinessAsync(
            options,
            tenantBId,
            businessBId);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }

    [Fact]
    public async Task Delete_WithForgedTenantId_ShouldNotDeleteAnotherTenantLocation()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessBId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            setupContext.Locations.Add(
                CreateLocation(
                    locationBId,
                    tenantBId,
                    businessBId,
                    "Location B"));

            await setupContext.SaveChangesAsync();
        }

        // Tenant A attempts to delete Location B using a detached
        // entity with a forged TenantId.
        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var forgedLocation = CreateLocation(
                locationBId,
                tenantAId,
                businessBId,
                "Forged Location");

            tenantAContext.Attach(forgedLocation);

            tenantAContext.Entry(forgedLocation).State =
                EntityState.Deleted;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => tenantAContext.SaveChangesAsync());
        }

        // Verify the physical state without relying on the query filter.
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var location = await verificationContext.Locations
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.Id == locationBId);

            Assert.NotNull(location);

            Assert.Equal(
                tenantBId,
                location.TenantId);

            Assert.Equal(
                "Location B",
                location.Name);
        }

        await RemoveLocationAsync(
            options,
            tenantBId,
            locationBId);

        await RemoveBusinessAsync(
            options,
            tenantBId,
            businessBId);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }

    [Fact]
    public async Task Insert_LocationReferencingBusinessFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessBId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await setupContext.SaveChangesAsync();
        }

        // A Location pertence ao Tenant A, portanto passa pela
        // validação do TenantContext. Porém, ela referencia um
        // Business pertencente ao Tenant B.
        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var invalidLocation = CreateLocation(
                locationAId,
                tenantAId,
                businessBId,
                "Invalid Location");

            tenantAContext.Locations.Add(invalidLocation);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => tenantAContext.SaveChangesAsync());
        }

        // Confirma fisicamente que a Location não foi persistida.
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var locationExists = await verificationContext.Locations
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == locationAId);

            Assert.False(locationExists);
        }

        await RemoveBusinessAsync(
            options,
            tenantBId,
            businessBId);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }
    // =========================================================
    // Helpers
    // =========================================================

    private DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
    }

    private static Business CreateBusiness(
        Guid id,
        Guid tenantId,
        string name)
    {
        return new Business(
            id,
            tenantId,
            BusinessTypeIds.Barbershop,
            name,
            BusinessCreatedAt);
    }

    private static Location CreateLocation(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string name)
    {
        return new Location(
            id,
            tenantId,
            businessId,
            name,
            "BR",
            "America/Sao_Paulo",
            LocationCreatedAt);
    }

    private static TenantContext CreateTenantContext(Guid tenantId)
    {
        var context = new TenantContext();

        context.Initialize(tenantId);

        return context;
    }

    private static async Task RemoveLocationAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid locationId)
    {
        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var location = await dbContext.Locations
            .SingleOrDefaultAsync(
                x => x.Id == locationId);

        if (location is null)
        {
            return;
        }

        dbContext.Locations.Remove(location);

        await dbContext.SaveChangesAsync();
    }

    private static async Task RemoveBusinessAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid businessId)
    {
        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var business = await dbContext.Businesses
            .SingleOrDefaultAsync(
                x => x.Id == businessId);

        if (business is null)
        {
            return;
        }

        dbContext.Businesses.Remove(business);

        await dbContext.SaveChangesAsync();
    }

    private static async Task RemoveTenantsAsync(
        DbContextOptions<AppDbContext> options,
        params Guid[] tenantIds)
    {
        await using var dbContext = new AppDbContext(
            options,
            new TenantContext());

        var tenants = await dbContext.Tenants
            .Where(x => tenantIds.Contains(x.Id))
            .ToListAsync();

        dbContext.Tenants.RemoveRange(tenants);

        await dbContext.SaveChangesAsync();
    }
}