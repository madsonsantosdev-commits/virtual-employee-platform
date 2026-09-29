using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.Businesses;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class BusinessEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public BusinessEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetBusiness_WithBusinessFromCurrentTenant_ShouldReturnOk()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                seedContext.Tenants.Add(
                    new Tenant(
                        tenantId,
                        "Business API Tenant"));

                seedContext.Businesses.Add(
                    CreateBusiness(
                        businessId,
                        tenantId,
                        "Barbearia Central"));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var response = await client.GetAsync(
                "/api/v1/business");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var business =
                await response.Content
                    .ReadFromJsonAsync<BusinessResponse>();

            Assert.NotNull(business);

            Assert.Equal(
                businessId,
                business.Id);

            Assert.Equal(
                BusinessTypeIds.Barbershop,
                business.BusinessTypeId);

            Assert.Equal(
                "Barbearia Central",
                business.Name);

            Assert.Equal(
                15,
                business.SlotIntervalMinutes);

            Assert.True(
                business.IsActive);
        }
        finally
        {
            await RemoveBusinessAsync(
                options,
                tenantId,
                businessId);

            await RemoveTenantAsync(
                options,
                tenantId);
        }
    }

    [Fact]
    public async Task GetBusiness_WithoutBusinessForCurrentTenant_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                seedContext.Tenants.Add(
                    new Tenant(
                        tenantId,
                        "Tenant Without Business"));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var response = await client.GetAsync(
                "/api/v1/business");

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }
        finally
        {
            await RemoveTenantAsync(
                options,
                tenantId);
        }
    }

    [Fact]
    public async Task GetBusiness_WithBusinessFromAnotherTenant_ShouldReturnNotFound()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var options = CreateOptions();

        try
        {
            await using (var tenantContext = new AppDbContext(
                options,
                new TenantContext()))
            {
                tenantContext.Tenants.AddRange(
                    new Tenant(
                        tenantAId,
                        "Business Tenant A"),
                    new Tenant(
                        tenantBId,
                        "Business Tenant B"));

                await tenantContext.SaveChangesAsync();
            }

            await using (var tenantBContext = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                tenantBContext.Businesses.Add(
                    CreateBusiness(
                        businessBId,
                        tenantBId,
                        "Business Tenant B"));

                await tenantBContext.SaveChangesAsync();
            }

            // Proves that the Business physically exists in PostgreSQL,
            // even though tenant A must not be able to see it.
            await using (var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var persistedBusiness =
                    await verificationContext.Businesses
                        .IgnoreQueryFilters()
                        .SingleOrDefaultAsync(
                            x => x.Id == businessBId);

                Assert.NotNull(persistedBusiness);

                Assert.Equal(
                    tenantBId,
                    persistedBusiness.TenantId);
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var response = await client.GetAsync(
                "/api/v1/business");

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }
        finally
        {
            await RemoveBusinessAsync(
                options,
                tenantBId,
                businessBId);

            await RemoveTenantAsync(
                options,
                tenantAId);

            await RemoveTenantAsync(
                options,
                tenantBId);
        }
    }

    [Fact]
    public async Task UpdateBusiness_WithBusinessFromCurrentTenant_ShouldReturnOk()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                seedContext.Tenants.Add(
                    new Tenant(
                        tenantId,
                        "Update Business Tenant"));

                seedContext.Businesses.Add(
                    CreateBusiness(
                        businessId,
                        tenantId,
                        "Barbearia Original"));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var response = await client.PutAsJsonAsync(
                "/api/v1/business",
                new
                {
                    businessTypeId = BusinessTypeIds.BeautySalon,
                    name = "  Salão Atualizado  ",
                    slotIntervalMinutes = 30,
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var business =
                await response.Content
                    .ReadFromJsonAsync<BusinessResponse>();

            Assert.NotNull(business);

            Assert.Equal(
                businessId,
                business.Id);

            Assert.Equal(
                BusinessTypeIds.BeautySalon,
                business.BusinessTypeId);

            Assert.Equal(
                "Salão Atualizado",
                business.Name);

            Assert.Equal(
                30,
                business.SlotIntervalMinutes);

            Assert.False(
                business.IsActive);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedBusiness =
                await verificationContext.Businesses
                    .SingleAsync(
                        x => x.Id == businessId);

            Assert.Equal(
                businessId,
                persistedBusiness.Id);

            Assert.Equal(
                tenantId,
                persistedBusiness.TenantId);

            Assert.Equal(
                BusinessTypeIds.BeautySalon,
                persistedBusiness.BusinessTypeId);

            Assert.Equal(
                "Salão Atualizado",
                persistedBusiness.Name);

            Assert.Equal(
                30,
                persistedBusiness.SlotIntervalMinutes);

            Assert.False(
                persistedBusiness.IsActive);

            Assert.Equal(
                CreatedAt,
                persistedBusiness.CreatedAt);

            Assert.True(
                persistedBusiness.UpdatedAt > CreatedAt);
        }
        finally
        {
            await RemoveBusinessAsync(
                options,
                tenantId,
                businessId);

            await RemoveTenantAsync(
                options,
                tenantId);
        }
    }

    [Fact]
    public async Task UpdateBusiness_WithoutBusinessForCurrentTenant_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                seedContext.Tenants.Add(
                    new Tenant(
                        tenantId,
                        "Tenant Without Business"));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var response = await client.PutAsJsonAsync(
                "/api/v1/business",
                new
                {
                    businessTypeId = BusinessTypeIds.BeautySalon,
                    name = "Business Inexistente",
                    slotIntervalMinutes = 30,
                    isActive = true
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }
        finally
        {
            await RemoveTenantAsync(
                options,
                tenantId);
        }
    }

    [Fact]
    public async Task UpdateBusiness_FromAnotherTenant_ShouldReturnNotFoundAndPreserveBusiness()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                new TenantContext()))
            {
                seedContext.Tenants.AddRange(
                    new Tenant(
                        tenantAId,
                        "Update Business Tenant A"),
                    new Tenant(
                        tenantBId,
                        "Update Business Tenant B"));

                await seedContext.SaveChangesAsync();
            }

            await using (var tenantBContext = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                tenantBContext.Businesses.Add(
                    CreateBusiness(
                        businessBId,
                        tenantBId,
                        "Business Original B"));

                await tenantBContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var response = await client.PutAsJsonAsync(
                "/api/v1/business",
                new
                {
                    businessTypeId = BusinessTypeIds.BeautySalon,
                    name = "Business Invadido",
                    slotIntervalMinutes = 60,
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId));

            var persistedBusiness =
                await verificationContext.Businesses
                    .IgnoreQueryFilters()
                    .SingleAsync(
                        x => x.Id == businessBId);

            Assert.Equal(
                tenantBId,
                persistedBusiness.TenantId);

            Assert.Equal(
                BusinessTypeIds.Barbershop,
                persistedBusiness.BusinessTypeId);

            Assert.Equal(
                "Business Original B",
                persistedBusiness.Name);

            Assert.Equal(
                15,
                persistedBusiness.SlotIntervalMinutes);

            Assert.True(
                persistedBusiness.IsActive);

            Assert.Equal(
                CreatedAt,
                persistedBusiness.CreatedAt);

            Assert.Equal(
                CreatedAt,
                persistedBusiness.UpdatedAt);
        }
        finally
        {
            await RemoveBusinessAsync(
                options,
                tenantBId,
                businessBId);

            await RemoveTenantAsync(
                options,
                tenantAId);

            await RemoveTenantAsync(
                options,
                tenantBId);
        }
    }

    [Fact]
    public async Task UpdateBusiness_WithUnknownBusinessType_ShouldReturnNotFoundAndPreserveBusiness()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                seedContext.Tenants.Add(
                    new Tenant(
                        tenantId,
                        "Unknown Business Type Tenant"));

                seedContext.Businesses.Add(
                    CreateBusiness(
                        businessId,
                        tenantId,
                        "Business Original"));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var response = await client.PutAsJsonAsync(
                "/api/v1/business",
                new
                {
                    businessTypeId = Guid.NewGuid(),
                    name = "Business Alterado",
                    slotIntervalMinutes = 30,
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedBusiness =
                await verificationContext.Businesses
                    .SingleAsync(
                        x => x.Id == businessId);

            Assert.Equal(
                BusinessTypeIds.Barbershop,
                persistedBusiness.BusinessTypeId);

            Assert.Equal(
                "Business Original",
                persistedBusiness.Name);

            Assert.Equal(
                15,
                persistedBusiness.SlotIntervalMinutes);

            Assert.True(
                persistedBusiness.IsActive);

            Assert.Equal(
                CreatedAt,
                persistedBusiness.UpdatedAt);
        }
        finally
        {
            await RemoveBusinessAsync(
                options,
                tenantId,
                businessId);

            await RemoveTenantAsync(
                options,
                tenantId);
        }
    }

    [Fact]
    public async Task UpdateBusiness_WithInvalidRequest_ShouldReturnBadRequestAndPreserveBusiness()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                seedContext.Tenants.Add(
                    new Tenant(
                        tenantId,
                        "Invalid Business Request Tenant"));

                seedContext.Businesses.Add(
                    CreateBusiness(
                        businessId,
                        tenantId,
                        "Business Original"));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var response = await client.PutAsJsonAsync(
                "/api/v1/business",
                new
                {
                    businessTypeId = BusinessTypeIds.BeautySalon,
                    name = " ",
                    slotIntervalMinutes = 0,
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var body =
                await response.Content.ReadAsStringAsync();

            Assert.Contains(
                "Invalid business request",
                body);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedBusiness =
                await verificationContext.Businesses
                    .SingleAsync(
                        x => x.Id == businessId);

            Assert.Equal(
                BusinessTypeIds.Barbershop,
                persistedBusiness.BusinessTypeId);

            Assert.Equal(
                "Business Original",
                persistedBusiness.Name);

            Assert.Equal(
                15,
                persistedBusiness.SlotIntervalMinutes);

            Assert.True(
                persistedBusiness.IsActive);

            Assert.Equal(
                CreatedAt,
                persistedBusiness.UpdatedAt);
        }
        finally
        {
            await RemoveBusinessAsync(
                options,
                tenantId,
                businessId);

            await RemoveTenantAsync(
                options,
                tenantId);
        }
    }
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.UseSetting(
                    "ConnectionStrings:Database",
                    _fixture.ConnectionString);

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<
                        DbContextOptions<AppDbContext>>();

                    services.RemoveAll<AppDbContext>();

                    services.AddDbContext<AppDbContext>(
                        options =>
                        {
                            options.UseNpgsql(
                                _fixture.ConnectionString);
                        });
                });
            });
    }

    private DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
    }

    private static TenantContext CreateTenantContext(
        Guid tenantId)
    {
        var tenantContext = new TenantContext();

        tenantContext.Initialize(tenantId);

        return tenantContext;
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

    private static async Task RemoveBusinessAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid businessId)
    {
        await using var context = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var business = await context.Businesses
            .SingleOrDefaultAsync(
                x => x.Id == businessId);

        if (business is null)
        {
            return;
        }

        context.Businesses.Remove(business);

        await context.SaveChangesAsync();
    }

    private static async Task RemoveTenantAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId)
    {
        await using var context = new AppDbContext(
            options,
            new TenantContext());

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(
                x => x.Id == tenantId);

        if (tenant is null)
        {
            return;
        }

        context.Tenants.Remove(tenant);

        await context.SaveChangesAsync();
    }
}