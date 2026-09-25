using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class LocationsEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public LocationsEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetLocations_ShouldReturnOnlyLocationsFromCurrentTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        await SeedAsync(
            tenantAId,
            tenantBId,
            businessAId,
            businessBId,
            locationAId,
            locationBId);

        try
        {
            // Proves that both tenant records physically exist
            // in the PostgreSQL integration-test database.
            var options = CreateOptions();

            await using (var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var persistedLocations =
                    await verificationContext.Locations
                        .IgnoreQueryFilters()
                        .Where(x =>
                            x.Id == locationAId ||
                            x.Id == locationBId)
                        .Select(x => new
                        {
                            x.Id,
                            x.TenantId,
                            x.BusinessId,
                            x.Name
                        })
                        .ToListAsync();

                Assert.Equal(
                    2,
                    persistedLocations.Count);

                Assert.Contains(
                    persistedLocations,
                    x =>
                        x.Id == locationAId &&
                        x.TenantId == tenantAId);

                Assert.Contains(
                    persistedLocations,
                    x =>
                        x.Id == locationBId &&
                        x.TenantId == tenantBId);
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var response = await client.GetAsync(
                "/api/v1/locations");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var locations =
                await response.Content
                    .ReadFromJsonAsync<List<LocationResponse>>();

            Assert.NotNull(locations);

            var location = Assert.Single(locations);

            Assert.Equal(
                locationAId,
                location.Id);

            Assert.Equal(
                businessAId,
                location.BusinessId);

            Assert.Equal(
                "Location A",
                location.Name);

            Assert.DoesNotContain(
                locations,
                x => x.Id == locationBId);
        }
        finally
        {
            await CleanupAsync(
                tenantAId,
                tenantBId,
                businessAId,
                businessBId,
                locationAId,
                locationBId);
        }
    }

    [Fact]
    public async Task GetLocations_WithoutTenantHeader_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/locations");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "TENANT_HEADER_REQUIRED",
            body);
    }

    [Fact]
    public async Task GetLocations_WithInvalidTenantHeader_ShouldReturnBadRequest()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add(
            "X-Tenant-Id",
            "tenant-invalido");

        var response = await client.GetAsync(
            "/api/v1/locations");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "INVALID_TENANT_ID",
            body);
    }

    [Fact]
    public async Task GetLocationById_ShouldReturnLocationFromCurrentTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        await SeedAsync(
            tenantAId,
            tenantBId,
            businessAId,
            businessBId,
            locationAId,
            locationBId);

        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var response = await client.GetAsync(
                $"/api/v1/locations/{locationAId}");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var location =
                await response.Content
                    .ReadFromJsonAsync<LocationResponse>();

            Assert.NotNull(location);

            Assert.Equal(
                locationAId,
                location.Id);

            Assert.Equal(
                businessAId,
                location.BusinessId);

            Assert.Equal(
                "Location A",
                location.Name);
        }
        finally
        {
            await CleanupAsync(
                tenantAId,
                tenantBId,
                businessAId,
                businessBId,
                locationAId,
                locationBId);
        }
    }

    [Fact]
    public async Task GetLocationById_FromAnotherTenant_ShouldReturnNotFound()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        await SeedAsync(
            tenantAId,
            tenantBId,
            businessAId,
            businessBId,
            locationAId,
            locationBId);

        try
        {
            var options = CreateOptions();

            await using (var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var persistedLocation =
                    await verificationContext.Locations
                        .IgnoreQueryFilters()
                        .SingleOrDefaultAsync(
                            x => x.Id == locationBId);

                Assert.NotNull(persistedLocation);

                Assert.Equal(
                    tenantBId,
                    persistedLocation.TenantId);
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var response = await client.GetAsync(
                $"/api/v1/locations/{locationBId}");

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }
        finally
        {
            await CleanupAsync(
                tenantAId,
                tenantBId,
                businessAId,
                businessBId,
                locationAId,
                locationBId);
        }
    }

    [Fact]
    public async Task GetLocationById_WithUnknownId_ShouldReturnNotFound()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add(
            "X-Tenant-Id",
            Guid.NewGuid().ToString());

        var response = await client.GetAsync(
            $"/api/v1/locations/{Guid.NewGuid()}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task CreateLocation_WithBusinessFromCurrentTenant_ShouldReturnCreated()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var options = CreateOptions();

        await using (var seedContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            seedContext.Tenants.Add(
                new Tenant(
                    tenantId,
                    "Create Location Tenant"));

            seedContext.Businesses.Add(
                CreateBusiness(
                    businessId,
                    tenantId,
                    "Create Location Business"));

            await seedContext.SaveChangesAsync();
        }

        Guid createdLocationId = Guid.Empty;

        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var request = new
            {
                businessId,
                name = "  Unidade Moema  ",
                countryCode = " br ",
                timezone = "  America/Sao_Paulo  ",
                phone = "  +5511999999999  ",
                address = "  Av. Exemplo, 100  "
            };

            var response = await client.PostAsJsonAsync(
                "/api/v1/locations",
                request);

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var location =
                await response.Content
                    .ReadFromJsonAsync<LocationResponse>();

            Assert.NotNull(location);

            createdLocationId = location.Id;

            Assert.NotEqual(
                Guid.Empty,
                location.Id);

            Assert.Equal(
                businessId,
                location.BusinessId);

            Assert.Equal(
                "Unidade Moema",
                location.Name);

            Assert.Equal(
                "+5511999999999",
                location.Phone);

            Assert.Equal(
                "Av. Exemplo, 100",
                location.Address);

            Assert.Equal(
                "BR",
                location.CountryCode);

            Assert.Equal(
                "America/Sao_Paulo",
                location.Timezone);

            Assert.True(
                location.IsActive);

            Assert.Equal(
                $"/api/v1/locations/{location.Id}",
                response.Headers.Location?.ToString());

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedLocation =
                await verificationContext.Locations
                    .IgnoreQueryFilters()
                    .SingleAsync(
                        x => x.Id == location.Id);

            Assert.Equal(
                tenantId,
                persistedLocation.TenantId);

            Assert.Equal(
                businessId,
                persistedLocation.BusinessId);
        }
        finally
        {
            if (createdLocationId != Guid.Empty)
            {
                await RemoveLocationAsync(
                    options,
                    tenantId,
                    createdLocationId);
            }

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
    public async Task CreateLocation_WithUnknownBusiness_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var unknownBusinessId = Guid.NewGuid();

        var options = CreateOptions();

        await using (var seedContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            seedContext.Tenants.Add(
                new Tenant(
                    tenantId,
                    "Unknown Business Tenant"));

            await seedContext.SaveChangesAsync();
        }

        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var request = new
            {
                businessId = unknownBusinessId,
                name = "Unidade Inexistente",
                countryCode = "BR",
                timezone = "America/Sao_Paulo",
                phone = (string?)null,
                address = (string?)null
            };

            var response = await client.PostAsJsonAsync(
                "/api/v1/locations",
                request);

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedLocation =
                await verificationContext.Locations
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(
                        x => x.BusinessId == unknownBusinessId);

            Assert.Null(persistedLocation);
        }
        finally
        {
            await RemoveTenantAsync(
                options,
                tenantId);
        }
    }

    [Fact]
    public async Task CreateLocation_WithBusinessFromAnotherTenant_ShouldReturnNotFound()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var options = CreateOptions();

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            tenantAContext.Tenants.AddRange(
                new Tenant(
                    tenantAId,
                    "Cross Tenant A"),
                new Tenant(
                    tenantBId,
                    "Cross Tenant B"));

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
                    "Business Tenant B"));

            await tenantBContext.SaveChangesAsync();
        }

        try
        {
            await using (var locationVerificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var persistedBusiness =
                    await locationVerificationContext.Businesses
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

            var request = new
            {
                businessId = businessBId,
                name = "Cross Tenant Location",
                countryCode = "BR",
                timezone = "America/Sao_Paulo",
                phone = (string?)null,
                address = (string?)null
            };

            var response = await client.PostAsJsonAsync(
                "/api/v1/locations",
                request);

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId));

            var persistedLocation =
                await verificationContext.Locations
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(
                        x => x.BusinessId == businessBId);

            Assert.Null(persistedLocation);
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
    public async Task CreateLocation_WithInvalidRequest_ShouldReturnBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var options = CreateOptions();

        await using (var seedContext = new AppDbContext(
            options,
            CreateTenantContext(tenantId)))
        {
            seedContext.Tenants.Add(
                new Tenant(
                    tenantId,
                    "Invalid Location Tenant"));

            seedContext.Businesses.Add(
                CreateBusiness(
                    businessId,
                    tenantId,
                    "Invalid Location Business"));

            await seedContext.SaveChangesAsync();
        }

        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var request = new
            {
                businessId,
                name = " ",
                countryCode = "BR",
                timezone = "America/Sao_Paulo",
                phone = (string?)null,
                address = (string?)null
            };

            var response = await client.PostAsJsonAsync(
                "/api/v1/locations",
                request);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var body =
                await response.Content.ReadAsStringAsync();

            Assert.Contains(
                "Invalid location request",
                body);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedLocations =
                await verificationContext.Locations
                    .IgnoreQueryFilters()
                    .Where(x => x.BusinessId == businessId)
                    .ToListAsync();

            Assert.Empty(persistedLocations);
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

    private async Task SeedAsync(
        Guid tenantAId,
        Guid tenantBId,
        Guid businessAId,
        Guid businessBId,
        Guid locationAId,
        Guid locationBId)
    {
        var options = CreateOptions();

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            tenantAContext.Tenants.AddRange(
                new Tenant(
                    tenantAId,
                    "HTTP Tenant A"),
                new Tenant(
                    tenantBId,
                    "HTTP Tenant B"));

            tenantAContext.Businesses.Add(
                CreateBusiness(
                    businessAId,
                    tenantAId,
                    "HTTP Business A"));

            await tenantAContext.SaveChangesAsync();

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
                    "HTTP Business B"));

            await tenantBContext.SaveChangesAsync();

            tenantBContext.Locations.Add(
                CreateLocation(
                    locationBId,
                    tenantBId,
                    businessBId,
                    "Location B"));

            await tenantBContext.SaveChangesAsync();
        }
    }

    private async Task CleanupAsync(
        Guid tenantAId,
        Guid tenantBId,
        Guid businessAId,
        Guid businessBId,
        Guid locationAId,
        Guid locationBId)
    {
        var options = CreateOptions();

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

        await using var context = new AppDbContext(
            options,
            new TenantContext());

        var tenants = await context.Tenants
            .Where(x =>
                x.Id == tenantAId ||
                x.Id == tenantBId)
            .ToListAsync();

        context.Tenants.RemoveRange(tenants);

        await context.SaveChangesAsync();
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

    private static async Task RemoveLocationAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid locationId)
    {
        await using var context = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var location = await context.Locations
            .SingleOrDefaultAsync(
                x => x.Id == locationId);

        if (location is null)
        {
            return;
        }

        context.Locations.Remove(location);

        await context.SaveChangesAsync();
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