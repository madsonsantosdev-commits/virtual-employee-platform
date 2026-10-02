using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using VirtualEmployee.Infrastructure.LocationServices;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class LocationServicePersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public LocationServicePersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LocationService_WithValidLocationAndService_ShouldBePersisted()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        await using (var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            dbContext.Tenants.Add(
                new Tenant(tenantId, "Tenant"));

            dbContext.Businesses.Add(
                CreateBusiness(
                    businessId,
                    tenantId,
                    "Business"));

            dbContext.Locations.Add(
                CreateLocation(
                    locationId,
                    tenantId,
                    businessId,
                    "Location"));

            dbContext.Services.Add(
                CreateService(
                    serviceId,
                    tenantId,
                    businessId,
                    "Service"));

            await dbContext.SaveChangesAsync();

            dbContext.LocationServices.Add(
                new LocationService(
                    tenantId,
                    locationId,
                    serviceId,
                    CreatedAt));

            await dbContext.SaveChangesAsync();
        }

        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var locationService =
                await verificationContext.LocationServices
                    .SingleAsync(x =>
                        x.LocationId == locationId &&
                        x.ServiceId == serviceId);

            Assert.Equal(tenantId, locationService.TenantId);
            Assert.Equal(locationId, locationService.LocationId);
            Assert.Equal(serviceId, locationService.ServiceId);
            Assert.Equal(CreatedAt, locationService.CreatedAt);
        }

        await CleanupAsync(
            options,
            tenantId,
            businessId,
            locationId,
            serviceId);
    }

    [Fact]
    public async Task LocationServices_ShouldBeIsolatedByTenant()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        await CreateCompleteScenarioAsync(
            options,
            tenantAId,
            businessAId,
            locationAId,
            serviceAId,
            "A",
            createTenant: true);

        await CreateCompleteScenarioAsync(
            options,
            tenantBId,
            businessBId,
            locationBId,
            serviceBId,
            "B",
            createTenant: true);

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var locationServices =
                await tenantAContext.LocationServices
                    .ToListAsync();

            var locationService = Assert.Single(locationServices);

            Assert.Equal(tenantAId, locationService.TenantId);
            Assert.Equal(locationAId, locationService.LocationId);
            Assert.Equal(serviceAId, locationService.ServiceId);
        }

        await using (var tenantBContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var locationServices =
                await tenantBContext.LocationServices
                    .ToListAsync();

            var locationService = Assert.Single(locationServices);

            Assert.Equal(tenantBId, locationService.TenantId);
            Assert.Equal(locationBId, locationService.LocationId);
            Assert.Equal(serviceBId, locationService.ServiceId);
        }

        await CleanupAsync(
            options,
            tenantAId,
            businessAId,
            locationAId,
            serviceAId);

        await CleanupAsync(
            options,
            tenantBId,
            businessBId,
            locationBId,
            serviceBId);
    }

    [Fact]
    public async Task Insert_DuplicateLocationService_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        await CreateCompleteScenarioAsync(
            options,
            tenantId,
            businessId,
            locationId,
            serviceId,
            string.Empty,
            createTenant: true);

        await using (var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            dbContext.LocationServices.Add(
                new LocationService(
                    tenantId,
                    locationId,
                    serviceId,
                    CreatedAt.AddMinutes(1)));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());
        }

        await CleanupAsync(
            options,
            tenantId,
            businessId,
            locationId,
            serviceId);
    }

    [Fact]
    public async Task Insert_LocationServiceReferencingLocationFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var locationBId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();

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

            tenantAContext.Services.Add(
                CreateService(
                    serviceAId,
                    tenantAId,
                    businessAId,
                    "Service A"));

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
            tenantAContext.LocationServices.Add(
                new LocationService(
                    tenantAId,
                    locationBId,
                    serviceAId,
                    CreatedAt));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => tenantAContext.SaveChangesAsync());
        }

        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var exists = await verificationContext.LocationServices
                .IgnoreQueryFilters()
                .AnyAsync(x =>
                    x.TenantId == tenantAId &&
                    x.LocationId == locationBId &&
                    x.ServiceId == serviceAId);

            Assert.False(exists);
        }

        await CleanupAsync(
            options,
            tenantAId,
            businessAId,
            null,
            serviceAId,
            removeTenant: false);

        await CleanupAsync(
            options,
            tenantBId,
            businessBId,
            locationBId,
            null,
            removeTenant: false);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }

    [Fact]
    public async Task Insert_LocationServiceReferencingServiceFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var locationAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

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
            tenantBContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            tenantBContext.Services.Add(
                CreateService(
                    serviceBId,
                    tenantBId,
                    businessBId,
                    "Service B"));

            await tenantBContext.SaveChangesAsync();
        }

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            tenantAContext.LocationServices.Add(
                new LocationService(
                    tenantAId,
                    locationAId,
                    serviceBId,
                    CreatedAt));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => tenantAContext.SaveChangesAsync());
        }

        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var exists = await verificationContext.LocationServices
                .IgnoreQueryFilters()
                .AnyAsync(x =>
                    x.TenantId == tenantAId &&
                    x.LocationId == locationAId &&
                    x.ServiceId == serviceBId);

            Assert.False(exists);
        }

        await CleanupAsync(
            options,
            tenantAId,
            businessAId,
            locationAId,
            null,
            removeTenant: false);

        await CleanupAsync(
            options,
            tenantBId,
            businessBId,
            null,
            serviceBId,
            removeTenant: false);

        await RemoveTenantsAsync(
            options,
            tenantAId,
            tenantBId);
    }
    [Fact]
    public async Task ReplaceAsync_ShouldReplaceLocationServicesAndRemainIdempotent()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();
        var serviceCId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            setupContext.Tenants.Add(
                new Tenant(
                    tenantId,
                    "Tenant"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessId,
                    tenantId,
                    "Business"));

            setupContext.Locations.Add(
                CreateLocation(
                    locationId,
                    tenantId,
                    businessId,
                    "Location"));

            setupContext.Services.AddRange(
                CreateService(
                    serviceAId,
                    tenantId,
                    businessId,
                    "Service A"),
                CreateService(
                    serviceBId,
                    tenantId,
                    businessId,
                    "Service B"),
                CreateService(
                    serviceCId,
                    tenantId,
                    businessId,
                    "Service C"));

            await setupContext.SaveChangesAsync();

            setupContext.LocationServices.AddRange(
                new LocationService(
                    tenantId,
                    locationId,
                    serviceAId,
                    CreatedAt),
                new LocationService(
                    tenantId,
                    locationId,
                    serviceBId,
                    CreatedAt));

            await setupContext.SaveChangesAsync();
        }

        // Estado inicial:
        // Location -> A, B
        //
        // Estado solicitado:
        // Location -> B, C
        await using (var updateContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var writeService =
                new LocationServiceWriteService(updateContext);

            await writeService.ReplaceAsync(
                locationId,
                [
                    serviceBId,
                    serviceCId
                ],
                CreatedAt.AddMinutes(1));

            await writeService.SaveChangesAsync();
        }

        // Verifica:
        // A removido
        // B preservado
        // C adicionado
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var links = await verificationContext.LocationServices
                .Where(x => x.LocationId == locationId)
                .ToListAsync();

            Assert.Equal(2, links.Count);

            Assert.Contains(
                links,
                x => x.ServiceId == serviceBId);

            Assert.Contains(
                links,
                x => x.ServiceId == serviceCId);

            Assert.DoesNotContain(
                links,
                x => x.ServiceId == serviceAId);
        }

        // Executa exatamente o mesmo replace novamente.
        // Deve permanecer B, C sem duplicações.
        await using (var secondUpdateContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var writeService =
                new LocationServiceWriteService(secondUpdateContext);

            await writeService.ReplaceAsync(
                locationId,
                [
                    serviceBId,
                    serviceCId
                ],
                CreatedAt.AddMinutes(2));

            await writeService.SaveChangesAsync();
        }

        // Verifica idempotência.
        await using (var finalVerificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var links = await finalVerificationContext.LocationServices
                .Where(x => x.LocationId == locationId)
                .ToListAsync();

            Assert.Equal(2, links.Count);

            Assert.Contains(
                links,
                x => x.ServiceId == serviceBId);

            Assert.Contains(
                links,
                x => x.ServiceId == serviceCId);

            Assert.DoesNotContain(
                links,
                x => x.ServiceId == serviceAId);
        }

        // Cleanup específico deste teste.
        //
        // Ordem necessária por causa das FKs:
        // LocationService
        // -> Location + Services
        // -> Business
        // -> Tenant
        await using (var cleanupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var locationServices = await cleanupContext.LocationServices
                .Where(x => x.LocationId == locationId)
                .ToListAsync();

            cleanupContext.LocationServices.RemoveRange(
                locationServices);

            await cleanupContext.SaveChangesAsync();

            var location = await cleanupContext.Locations
                .SingleAsync(x => x.Id == locationId);

            cleanupContext.Locations.Remove(location);

            var services = await cleanupContext.Services
                .Where(x =>
                    x.Id == serviceAId ||
                    x.Id == serviceBId ||
                    x.Id == serviceCId)
                .ToListAsync();

            cleanupContext.Services.RemoveRange(
                services);

            await cleanupContext.SaveChangesAsync();

            var business = await cleanupContext.Businesses
                .SingleAsync(x => x.Id == businessId);

            cleanupContext.Businesses.Remove(business);

            await cleanupContext.SaveChangesAsync();
        }

        await RemoveTenantsAsync(
            options,
            tenantId);
    }

    private async Task CreateCompleteScenarioAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid businessId,
        Guid locationId,
        Guid serviceId,
        string suffix,
        bool createTenant)
    {
        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        if (createTenant)
        {
            dbContext.Tenants.Add(
                new Tenant(
                    tenantId,
                    $"Tenant {suffix}".TrimEnd()));
        }

        dbContext.Businesses.Add(
            CreateBusiness(
                businessId,
                tenantId,
                $"Business {suffix}".TrimEnd()));

        dbContext.Locations.Add(
            CreateLocation(
                locationId,
                tenantId,
                businessId,
                $"Location {suffix}".TrimEnd()));

        dbContext.Services.Add(
            CreateService(
                serviceId,
                tenantId,
                businessId,
                $"Service {suffix}".TrimEnd()));

        await dbContext.SaveChangesAsync();

        dbContext.LocationServices.Add(
            new LocationService(
                tenantId,
                locationId,
                serviceId,
                CreatedAt));

        await dbContext.SaveChangesAsync();
    }

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
            CreatedAt);
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
            CreatedAt);
    }

    private static Service CreateService(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string name)
    {
        return new Service(
            id,
            tenantId,
            businessId,
            name,
            ServiceType.Single,
            100m,
            60,
            CreatedAt);
    }

    private static TenantContext CreateTenantContext(Guid tenantId)
    {
        var context = new TenantContext();
        context.Initialize(tenantId);

        return context;
    }

    private static async Task CleanupAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid businessId,
        Guid? locationId,
        Guid? serviceId,
        bool removeTenant = true)
    {
        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var locationServices = await dbContext.LocationServices
            .ToListAsync();

        dbContext.LocationServices.RemoveRange(locationServices);

        await dbContext.SaveChangesAsync();

        if (locationId.HasValue)
        {
            var location = await dbContext.Locations
                .SingleOrDefaultAsync(x => x.Id == locationId.Value);

            if (location is not null)
            {
                dbContext.Locations.Remove(location);
            }
        }

        if (serviceId.HasValue)
        {
            var service = await dbContext.Services
                .SingleOrDefaultAsync(x => x.Id == serviceId.Value);

            if (service is not null)
            {
                dbContext.Services.Remove(service);
            }
        }

        await dbContext.SaveChangesAsync();

        var business = await dbContext.Businesses
            .SingleOrDefaultAsync(x => x.Id == businessId);

        if (business is not null)
        {
            dbContext.Businesses.Remove(business);
            await dbContext.SaveChangesAsync();
        }

        if (removeTenant)
        {
            await RemoveTenantsAsync(
                options,
                tenantId);
        }
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