using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Services;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class ServicePersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ServicePersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Services_ShouldBeIsolatedByTenant()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var serviceAId = Guid.NewGuid();
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
            var services = await tenantAContext.Services
                .Where(x =>
                    x.Id == serviceAId ||
                    x.Id == serviceBId)
                .ToListAsync();

            var service = Assert.Single(services);

            Assert.Equal(serviceAId, service.Id);
            Assert.Equal(tenantAId, service.TenantId);
        }

        await using (var tenantBContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var services = await tenantBContext.Services
                .Where(x =>
                    x.Id == serviceAId ||
                    x.Id == serviceBId)
                .ToListAsync();

            var service = Assert.Single(services);

            Assert.Equal(serviceBId, service.Id);
            Assert.Equal(tenantBId, service.TenantId);
        }

        await RemoveServiceAsync(
            options,
            tenantAId,
            serviceAId);

        await RemoveServiceAsync(
            options,
            tenantBId,
            serviceBId);

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
    public async Task SaveChanges_WithServiceFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId));

        var serviceB = CreateService(
            Guid.NewGuid(),
            tenantBId,
            Guid.NewGuid(),
            "Service B");

        dbContext.Services.Add(serviceB);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task Insert_ServiceReferencingBusinessFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessBId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();

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
            var invalidService = CreateService(
                serviceAId,
                tenantAId,
                businessBId,
                "Invalid Service");

            tenantAContext.Services.Add(invalidService);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => tenantAContext.SaveChangesAsync());
        }

        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var serviceExists = await verificationContext.Services
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == serviceAId);

            Assert.False(serviceExists);
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
    public async Task ServiceComponent_WithValidServices_ShouldBePersisted()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var comboId = Guid.NewGuid();
        var componentId = Guid.NewGuid();

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

            dbContext.Services.AddRange(
                CreateService(
                    comboId,
                    tenantId,
                    businessId,
                    "Combo",
                    ServiceType.Combo),
                CreateService(
                    componentId,
                    tenantId,
                    businessId,
                    "Component"));

            await dbContext.SaveChangesAsync();

            dbContext.ServiceComponents.Add(
                new ServiceComponent(
                    tenantId,
                    comboId,
                    componentId,
                    0,
                    CreatedAt));

            await dbContext.SaveChangesAsync();
        }

        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var serviceComponent =
                await verificationContext.ServiceComponents
                    .SingleAsync(x =>
                        x.ComboServiceId == comboId &&
                        x.ComponentServiceId == componentId);

            Assert.Equal(tenantId, serviceComponent.TenantId);
            Assert.Equal(0, serviceComponent.SortOrder);
        }

        await RemoveServiceComponentAsync(
            options,
            tenantId,
            comboId,
            componentId);

        await RemoveServiceAsync(
            options,
            tenantId,
            comboId);

        await RemoveServiceAsync(
            options,
            tenantId,
            componentId);

        await RemoveBusinessAsync(
            options,
            tenantId,
            businessId);

        await RemoveTenantsAsync(
            options,
            tenantId);
    }

    [Fact]
    public async Task Insert_ServiceComponentReferencingServiceFromAnotherTenant_ShouldThrow()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var comboAId = Guid.NewGuid();
        var componentBId = Guid.NewGuid();

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
                    comboAId,
                    tenantAId,
                    businessAId,
                    "Combo A",
                    ServiceType.Combo));

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
                    componentBId,
                    tenantBId,
                    businessBId,
                    "Component B"));

            await tenantBContext.SaveChangesAsync();
        }

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var invalidComponent = new ServiceComponent(
                tenantAId,
                comboAId,
                componentBId,
                0,
                CreatedAt);

            tenantAContext.ServiceComponents.Add(invalidComponent);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => tenantAContext.SaveChangesAsync());
        }

        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var componentExists =
                await verificationContext.ServiceComponents
                    .IgnoreQueryFilters()
                    .AnyAsync(x =>
                        x.TenantId == tenantAId &&
                        x.ComboServiceId == comboAId &&
                        x.ComponentServiceId == componentBId);

            Assert.False(componentExists);
        }

        await RemoveServiceAsync(
            options,
            tenantAId,
            comboAId);

        await RemoveServiceAsync(
            options,
            tenantBId,
            componentBId);

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
    public async Task ReplaceComponentsAsync_ShouldRemoveAddAndReorderComponents()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var comboId = Guid.NewGuid();

        var componentAId = Guid.NewGuid();
        var componentBId = Guid.NewGuid();
        var componentCId = Guid.NewGuid();

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

            setupContext.Services.AddRange(
                CreateService(
                    comboId,
                    tenantId,
                    businessId,
                    "Combo",
                    ServiceType.Combo),
                CreateService(
                    componentAId,
                    tenantId,
                    businessId,
                    "Component A"),
                CreateService(
                    componentBId,
                    tenantId,
                    businessId,
                    "Component B"),
                CreateService(
                    componentCId,
                    tenantId,
                    businessId,
                    "Component C"));

            await setupContext.SaveChangesAsync();

            setupContext.ServiceComponents.AddRange(
                new ServiceComponent(
                    tenantId,
                    comboId,
                    componentAId,
                    0,
                    CreatedAt),
                new ServiceComponent(
                    tenantId,
                    comboId,
                    componentBId,
                    1,
                    CreatedAt));

            await setupContext.SaveChangesAsync();
        }

        await using (var updateContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var writeService =
                new ServiceWriteService(updateContext);

            await writeService.ReplaceComponentsAsync(
                comboId,
                [
                    componentBId,
                    componentCId
                ],
                CreatedAt.AddMinutes(1));

            await writeService.SaveChangesAsync();
        }

        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            var components =
                await verificationContext.ServiceComponents
                    .Where(x => x.ComboServiceId == comboId)
                    .OrderBy(x => x.SortOrder)
                    .ToListAsync();

            Assert.Equal(2, components.Count);

            Assert.Equal(
                componentBId,
                components[0].ComponentServiceId);

            Assert.Equal(
                0,
                components[0].SortOrder);

            Assert.Equal(
                componentCId,
                components[1].ComponentServiceId);

            Assert.Equal(
                1,
                components[1].SortOrder);

            Assert.DoesNotContain(
                components,
                x => x.ComponentServiceId == componentAId);
        }

        await RemoveServiceComponentAsync(
            options,
            tenantId,
            comboId,
            componentBId);

        await RemoveServiceComponentAsync(
            options,
            tenantId,
            comboId,
            componentCId);

        await RemoveServiceAsync(
            options,
            tenantId,
            comboId);

        await RemoveServiceAsync(
            options,
            tenantId,
            componentAId);

        await RemoveServiceAsync(
            options,
            tenantId,
            componentBId);

        await RemoveServiceAsync(
            options,
            tenantId,
            componentCId);

        await RemoveBusinessAsync(
            options,
            tenantId,
            businessId);

        await RemoveTenantsAsync(
            options,
            tenantId);
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

    private static Service CreateService(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string name,
        ServiceType serviceType = ServiceType.Single)
    {
        return new Service(
            id,
            tenantId,
            businessId,
            name,
            serviceType,
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

    private static async Task RemoveServiceComponentAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid comboServiceId,
        Guid componentServiceId)
    {
        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var component = await dbContext.ServiceComponents
            .SingleOrDefaultAsync(x =>
                x.ComboServiceId == comboServiceId &&
                x.ComponentServiceId == componentServiceId);

        if (component is null)
        {
            return;
        }

        dbContext.ServiceComponents.Remove(component);

        await dbContext.SaveChangesAsync();
    }

    private static async Task RemoveServiceAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid serviceId)
    {
        await using var dbContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var service = await dbContext.Services
            .SingleOrDefaultAsync(x => x.Id == serviceId);

        if (service is null)
        {
            return;
        }

        dbContext.Services.Remove(service);

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
            .SingleOrDefaultAsync(x => x.Id == businessId);

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