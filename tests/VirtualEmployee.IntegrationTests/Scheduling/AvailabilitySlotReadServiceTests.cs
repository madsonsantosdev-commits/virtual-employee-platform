using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.AvailabilitySlots;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Scheduling;

[Collection(PostgreSqlCollection.Name)]
public sealed class AvailabilitySlotReadServiceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public AvailabilitySlotReadServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task GetEligibleWindows_ShouldRequireAllServicesAndActiveRulesForRequestedDay()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var eligibleProfessionalId = Guid.NewGuid();
        var partialProfessionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await using (var context = CreateContext(tenantId))
            {
                context.Tenants.Add(
                    new Tenant(tenantId, "Slot Tenant"));

                context.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Slot Business",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.Locations.Add(
                    new Location(
                        locationId, tenantId, businessId,
                        "Location", "BR", "America/Sao_Paulo",
                        CreatedAt));

                context.Professionals.AddRange(
                    new Professional(
                        eligibleProfessionalId, tenantId, businessId,
                        "Eligible", CreatedAt),
                    new Professional(
                        partialProfessionalId, tenantId, businessId,
                        "Partial", CreatedAt));

                context.Services.AddRange(
                    new Service(
                        serviceAId, tenantId, businessId,
                        "Service A", ServiceType.Single,
                        30m, 20, CreatedAt),
                    new Service(
                        serviceBId, tenantId, businessId,
                        "Service B", ServiceType.Single,
                        60m, 40, CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.AddRange(
                    new ProfessionalLocation(
                        tenantId, eligibleProfessionalId,
                        locationId, CreatedAt),
                    new ProfessionalLocation(
                        tenantId, partialProfessionalId,
                        locationId, CreatedAt));

                context.LocationServices.AddRange(
                    new LocationService(
                        tenantId, locationId, serviceAId, CreatedAt),
                    new LocationService(
                        tenantId, locationId, serviceBId, CreatedAt));

                context.ProfessionalServices.AddRange(
                    new ProfessionalService(
                        tenantId, eligibleProfessionalId,
                        serviceAId, CreatedAt),
                    new ProfessionalService(
                        tenantId, eligibleProfessionalId,
                        serviceBId, CreatedAt),
                    new ProfessionalService(
                        tenantId, partialProfessionalId,
                        serviceAId, CreatedAt));

                await context.SaveChangesAsync();

                var inactiveRule = new AvailabilityRule(
                    Guid.NewGuid(), tenantId, locationId,
                    eligibleProfessionalId, DayOfWeek.Monday,
                    new TimeOnly(13, 0), new TimeOnly(18, 0),
                    CreatedAt);

                inactiveRule.Update(
                    DayOfWeek.Monday,
                    new TimeOnly(13, 0),
                    new TimeOnly(18, 0),
                    false,
                    CreatedAt.AddHours(1));

                context.AvailabilityRules.AddRange(
                    new AvailabilityRule(
                        Guid.NewGuid(), tenantId, locationId,
                        eligibleProfessionalId, DayOfWeek.Monday,
                        new TimeOnly(9, 0), new TimeOnly(12, 0),
                        CreatedAt),
                    new AvailabilityRule(
                        Guid.NewGuid(), tenantId, locationId,
                        eligibleProfessionalId, DayOfWeek.Tuesday,
                        new TimeOnly(9, 0), new TimeOnly(12, 0),
                        CreatedAt),
                    new AvailabilityRule(
                        Guid.NewGuid(), tenantId, locationId,
                        partialProfessionalId, DayOfWeek.Monday,
                        new TimeOnly(9, 0), new TimeOnly(12, 0),
                        CreatedAt),
                    inactiveRule);

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(tenantId);
            var readService = new AvailabilitySlotReadService(readContext);

            Guid[] serviceIds = [serviceAId, serviceBId];

            Assert.True(
                await readService.AreServicesOfferedAtLocationAsync(
                    locationId,
                    serviceIds));

            Assert.False(
                await readService.AreServicesOfferedAtLocationAsync(
                    locationId,
                    [serviceAId, Guid.NewGuid()]));

            var windows = await readService.GetEligibleWindowsAsync(
                locationId,
                businessId,
                serviceIds,
                DayOfWeek.Monday,
                professionalId: null);

            var window = Assert.Single(windows);

            Assert.Equal(eligibleProfessionalId, window.ProfessionalId);
            Assert.Equal(new TimeOnly(9, 0), window.StartTime);
            Assert.Equal(new TimeOnly(12, 0), window.EndTime);

            var partialWindows = await readService.GetEligibleWindowsAsync(
                locationId,
                businessId,
                serviceIds,
                DayOfWeek.Monday,
                partialProfessionalId);

            Assert.Empty(partialWindows);

            // Outro tenant não pode consultar os recursos do proprietário.
            var otherTenantId = Guid.NewGuid();

            await using var otherContext = CreateContext(otherTenantId);
            var otherReadService = new AvailabilitySlotReadService(otherContext);

            Assert.Null(
                await otherReadService.GetLocationByIdAsync(locationId));

            Assert.Null(
                await otherReadService.GetBusinessByIdAsync(businessId));

            Assert.Empty(
                await otherReadService.GetServicesByIdsAsync(serviceIds));

            Assert.False(
                await otherReadService.AreServicesOfferedAtLocationAsync(
                    locationId,
                    serviceIds));

            var otherTenantWindows =
                await otherReadService.GetEligibleWindowsAsync(
                    locationId,
                    businessId,
                    serviceIds,
                    DayOfWeek.Monday,
                    professionalId: null);

            Assert.Empty(otherTenantWindows);

            var selectedProfessionalWindows =
                await otherReadService.GetEligibleWindowsAsync(
                    locationId,
                    businessId,
                    serviceIds,
                    DayOfWeek.Monday,
                    eligibleProfessionalId);

            Assert.Empty(selectedProfessionalWindows);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    #endregion

    #region Helpers

    private AppDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return new AppDbContext(options, tenantContext);
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        context.AvailabilityRules.RemoveRange(
            await context.AvailabilityRules.ToListAsync());
        await context.SaveChangesAsync();

        context.ProfessionalServices.RemoveRange(
            await context.ProfessionalServices.ToListAsync());
        context.ProfessionalLocations.RemoveRange(
            await context.ProfessionalLocations.ToListAsync());
        context.LocationServices.RemoveRange(
            await context.LocationServices.ToListAsync());
        await context.SaveChangesAsync();

        context.Services.RemoveRange(
            await context.Services.ToListAsync());
        context.Professionals.RemoveRange(
            await context.Professionals.ToListAsync());
        context.Locations.RemoveRange(
            await context.Locations.ToListAsync());
        await context.SaveChangesAsync();

        context.Businesses.RemoveRange(
            await context.Businesses.ToListAsync());
        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(x => x.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }

    #endregion
}