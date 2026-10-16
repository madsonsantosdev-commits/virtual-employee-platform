using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.BookingQuotes;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Xunit;

namespace VirtualEmployee.IntegrationTests.BookingQuotes;

[Collection(PostgreSqlCollection.Name)]
public sealed class BookingQuoteReadServiceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public BookingQuoteReadServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReadService_ShouldReadResourcesAndRequireEveryLocationService()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                serviceAId,
                serviceBId);

            await using (var context = CreateContext(tenantId))
            {
                var readService = new BookingQuoteReadService(context);

                var location = await readService.GetLocationByIdAsync(
                    locationId);

                var business = await readService.GetBusinessByIdAsync(
                    businessId);

                Assert.NotNull(location);
                Assert.NotNull(business);
                Assert.Equal(businessId, location.BusinessId);
                Assert.Equal(tenantId, location.TenantId);
                Assert.Equal(tenantId, business.TenantId);

                var services = await readService.GetServicesByIdsAsync(
                    [serviceAId, serviceBId]);

                Assert.Equal(2, services.Count);

                var serviceA = Assert.Single(
                    services,
                    service => service.Id == serviceAId);

                Assert.Equal(50m, serviceA.Price);
                Assert.Equal(45, serviceA.DurationMinutes);

                var serviceB = Assert.Single(
                    services,
                    service => service.Id == serviceBId);

                Assert.Equal(30m, serviceB.Price);
                Assert.Equal(20, serviceB.DurationMinutes);

                Assert.True(
                    await readService.AreServicesOfferedAtLocationAsync(
                        locationId,
                        [serviceAId]));

                Assert.False(
                    await readService.AreServicesOfferedAtLocationAsync(
                        locationId,
                        [serviceAId, serviceBId]));

                Assert.False(
                    await readService.AreServicesOfferedAtLocationAsync(
                        locationId,
                        Array.Empty<Guid>()));

                Assert.Empty(context.ChangeTracker.Entries());
            }

            await using (var context = CreateContext(tenantId))
            {
                context.LocationServices.Add(
                    new LocationService(
                        tenantId,
                        locationId,
                        serviceBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var verificationContext = CreateContext(tenantId);
            var verificationService =
                new BookingQuoteReadService(verificationContext);

            Assert.True(
                await verificationService.AreServicesOfferedAtLocationAsync(
                    locationId,
                    [serviceAId, serviceBId]));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ReadService_ShouldIsolateResourcesAndLinksByTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();
        var serviceA1Id = Guid.NewGuid();
        var serviceA2Id = Guid.NewGuid();
        var serviceB1Id = Guid.NewGuid();
        var serviceB2Id = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                locationAId,
                serviceA1Id,
                serviceA2Id);

            await SeedAsync(
                tenantBId,
                businessBId,
                locationBId,
                serviceB1Id,
                serviceB2Id);

            await using (var contextA = CreateContext(tenantAId))
            {
                var service = new BookingQuoteReadService(contextA);

                Assert.NotNull(
                    await service.GetLocationByIdAsync(locationAId));

                Assert.Null(
                    await service.GetLocationByIdAsync(locationBId));

                Assert.NotNull(
                    await service.GetBusinessByIdAsync(businessAId));

                Assert.Null(
                    await service.GetBusinessByIdAsync(businessBId));

                var services = await service.GetServicesByIdsAsync(
                    [serviceA1Id, serviceB1Id]);

                Assert.Equal(serviceA1Id, Assert.Single(services).Id);

                Assert.True(
                    await service.AreServicesOfferedAtLocationAsync(
                        locationAId,
                        [serviceA1Id]));

                Assert.False(
                    await service.AreServicesOfferedAtLocationAsync(
                        locationBId,
                        [serviceB1Id]));

                Assert.False(
                    await service.AreServicesOfferedAtLocationAsync(
                        locationAId,
                        [serviceA1Id, serviceB1Id]));
            }

            await using (var contextB = CreateContext(tenantBId))
            {
                var service = new BookingQuoteReadService(contextB);

                Assert.NotNull(
                    await service.GetLocationByIdAsync(locationBId));

                Assert.Null(
                    await service.GetLocationByIdAsync(locationAId));

                Assert.NotNull(
                    await service.GetBusinessByIdAsync(businessBId));

                Assert.Null(
                    await service.GetBusinessByIdAsync(businessAId));

                var services = await service.GetServicesByIdsAsync(
                    [serviceA1Id, serviceB1Id]);

                Assert.Equal(serviceB1Id, Assert.Single(services).Id);

                Assert.True(
                    await service.AreServicesOfferedAtLocationAsync(
                        locationBId,
                        [serviceB1Id]));

                Assert.False(
                    await service.AreServicesOfferedAtLocationAsync(
                        locationAId,
                        [serviceA1Id]));
            }
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    private AppDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return new AppDbContext(options, tenantContext);
    }

    private async Task SeedAsync(
        Guid tenantId,
        Guid businessId,
        Guid locationId,
        Guid serviceAId,
        Guid serviceBId)
    {
        await using var context = CreateContext(tenantId);

        context.Tenants.Add(
            new Tenant(tenantId, "Quote Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Quote Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Locations.Add(
            new Location(
                locationId,
                tenantId,
                businessId,
                "Quote Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));

        context.Services.AddRange(
            new Service(
                serviceAId,
                tenantId,
                businessId,
                "Corte",
                ServiceType.Single,
                50m,
                45,
                CreatedAt),
            new Service(
                serviceBId,
                tenantId,
                businessId,
                "Barba",
                ServiceType.Single,
                30m,
                20,
                CreatedAt));

        await context.SaveChangesAsync();

        context.LocationServices.Add(
            new LocationService(
                tenantId,
                locationId,
                serviceAId,
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        context.LocationServices.RemoveRange(
            await context.LocationServices.ToListAsync());

        await context.SaveChangesAsync();

        context.Services.RemoveRange(
            await context.Services.ToListAsync());

        context.Locations.RemoveRange(
            await context.Locations.ToListAsync());

        await context.SaveChangesAsync();

        context.Businesses.RemoveRange(
            await context.Businesses.ToListAsync());

        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(entity => entity.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }
}