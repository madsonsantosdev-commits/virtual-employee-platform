using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProfessionalsEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ProfessionalsEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProfessionals_ShouldReturnOnlyProfessionalsFromCurrentTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            // Confirms that both records physically exist.
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                var professionals = await context.Professionals
                    .IgnoreQueryFilters()
                    .Where(x =>
                        x.Id == professionalAId ||
                        x.Id == professionalBId)
                    .ToListAsync();

                Assert.Equal(2, professionals.Count);

                Assert.Contains(
                    professionals,
                    x => x.Id == professionalAId &&
                         x.TenantId == tenantAId);

                Assert.Contains(
                    professionals,
                    x => x.Id == professionalBId &&
                         x.TenantId == tenantBId);
            }

            await using var factory = CreateFactory();

            using var clientA = factory.CreateClient();
            clientA.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var responseA = await clientA.GetAsync(
                "/api/v1/professionals");

            Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);

            var professionalsA = await responseA.Content
                .ReadFromJsonAsync<List<ProfessionalResponse>>();

            Assert.NotNull(professionalsA);

            var professionalA = Assert.Single(professionalsA);

            Assert.Equal(professionalAId, professionalA.Id);
            Assert.Equal(businessAId, professionalA.BusinessId);
            Assert.Equal("Professional A", professionalA.Name);
            Assert.True(professionalA.IsActive);

            using var clientB = factory.CreateClient();
            clientB.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantBId.ToString());

            using var responseB = await clientB.GetAsync(
                "/api/v1/professionals");

            Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

            var professionalsB = await responseB.Content
                .ReadFromJsonAsync<List<ProfessionalResponse>>();

            Assert.NotNull(professionalsB);

            var professionalB = Assert.Single(professionalsB);

            Assert.Equal(professionalBId, professionalB.Id);
            Assert.Equal(businessBId, professionalB.BusinessId);
            Assert.Equal("Professional B", professionalB.Name);
            Assert.True(professionalB.IsActive);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task ProfessionalLocations_WithValidLocations_ShouldReplaceAndReturnOrderedList()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Locations.AddRange(
                    new Location(
                        locationAId,
                        tenantId,
                        businessId,
                        "Alpha",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt),
                    new Location(
                        locationBId,
                        tenantId,
                        businessId,
                        "Beta",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var url = $"/api/v1/professionals/{professionalId}/locations";

            using var putResponse = await client.PutAsJsonAsync(
                url,
                new
                {
                    locationIds = new[] { locationBId, locationAId }
                });

            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

            using var getResponse = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var locations = await getResponse.Content
                .ReadFromJsonAsync<List<LocationResponse>>();

            Assert.NotNull(locations);
            Assert.Equal(2, locations.Count);

            Assert.Equal(
                new[] { locationAId, locationBId },
                locations.Select(location => location.Id).ToArray());

            Assert.Equal(
                new[] { "Alpha", "Beta" },
                locations.Select(location => location.Name).ToArray());

            Assert.All(locations, location =>
            {
                Assert.Equal(businessId, location.BusinessId);
                Assert.Equal("BR", location.CountryCode);
                Assert.Equal("America/Sao_Paulo", location.Timezone);
                Assert.True(location.IsActive);
            });

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .Where(link => link.ProfessionalId == professionalId)
                .ToListAsync();

            Assert.Equal(2, links.Count);
            Assert.Contains(links, link => link.LocationId == locationAId);
            Assert.Contains(links, link => link.LocationId == locationBId);

            Assert.All(links, link =>
            {
                Assert.Equal(tenantId, link.TenantId);
                Assert.Equal(professionalId, link.ProfessionalId);
                Assert.True(link.IsActive);
                Assert.Equal(link.CreatedAt, link.UpdatedAt);
            });
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalLocations_WithEmptyList_ShouldDeactivateAndAllowReactivation()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Locations.Add(
                    new Location(
                        locationId,
                        tenantId,
                        businessId,
                        "Location",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var url = $"/api/v1/professionals/{professionalId}/locations";

            using var emptyPutResponse = await client.PutAsJsonAsync(
                url,
                new { locationIds = Array.Empty<Guid>() });

            Assert.Equal(HttpStatusCode.OK, emptyPutResponse.StatusCode);

            using var emptyGetResponse = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, emptyGetResponse.StatusCode);

            var emptyLocations = await emptyGetResponse.Content
                .ReadFromJsonAsync<List<LocationResponse>>();

            Assert.NotNull(emptyLocations);
            Assert.Empty(emptyLocations);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalLocations
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(tenantId, link.TenantId);
                Assert.Equal(professionalId, link.ProfessionalId);
                Assert.Equal(locationId, link.LocationId);
                Assert.False(link.IsActive);
                Assert.Equal(CreatedAt, link.CreatedAt);
                Assert.True(link.UpdatedAt > CreatedAt);
            }

            using var reactivateResponse = await client.PutAsJsonAsync(
                url,
                new { locationIds = new[] { locationId } });

            Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);

            using var finalGetResponse = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, finalGetResponse.StatusCode);

            var locations = await finalGetResponse.Content
                .ReadFromJsonAsync<List<LocationResponse>>();

            Assert.NotNull(locations);

            var location = Assert.Single(locations);

            Assert.Equal(locationId, location.Id);
            Assert.Equal(businessId, location.BusinessId);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var persistedLink = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(tenantId, persistedLink.TenantId);
            Assert.Equal(professionalId, persistedLink.ProfessionalId);
            Assert.Equal(locationId, persistedLink.LocationId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.True(persistedLink.UpdatedAt > CreatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty-guid")]
    [InlineData("duplicate")]
    public async Task ProfessionalLocations_WithInvalidRequest_ShouldReturnBadRequestAndPreserveLink(
        string invalidCase)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Locations.Add(
                    new Location(
                        locationId,
                        tenantId,
                        businessId,
                        "Location",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            object payload = invalidCase switch
            {
                "missing" => new { },
                "null" => new { locationIds = (Guid[]?)null },
                "empty-guid" => new
                {
                    locationIds = new[] { locationId, Guid.Empty }
                },
                "duplicate" => new
                {
                    locationIds = new[] { locationId, locationId }
                },
                _ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
            };

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/locations",
                payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var link = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(locationId, link.LocationId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalLocations_WithLocationFromAnotherBusiness_ShouldReturnConflictAndPreserveLink()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var anotherBusinessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var anotherLocationId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Businesses.Add(
                    new Business(
                        anotherBusinessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Another Business",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.Locations.AddRange(
                    new Location(
                        locationId,
                        tenantId,
                        businessId,
                        "Current Location",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt),
                    new Location(
                        anotherLocationId,
                        tenantId,
                        anotherBusinessId,
                        "Another Business Location",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/locations",
                new
                {
                    locationIds = new[] { locationId, anotherLocationId }
                });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var otherLocation = await verificationContext.Locations
                .AsNoTracking()
                .SingleAsync(location => location.Id == anotherLocationId);

            Assert.Equal(anotherBusinessId, otherLocation.BusinessId);

            var link = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(locationId, link.LocationId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_WithValidServices_ShouldReplaceAndReturnOrderedList()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.AddRange(
                    new Service(
                        serviceAId,
                        tenantId,
                        businessId,
                        "Service A",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt),
                    new Service(
                        serviceBId,
                        tenantId,
                        businessId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();

            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var route =
                $"/api/v1/professionals/{professionalId}/services";

            using var putResponse = await client.PutAsJsonAsync(
                route,
                new
                {
                    serviceIds = new[] { serviceBId, serviceAId }
                });

            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
            Assert.Empty(await putResponse.Content.ReadAsStringAsync());

            using var getResponse = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var jsonOptions = new JsonSerializerOptions(
                JsonSerializerDefaults.Web);

            jsonOptions.Converters.Add(
                new JsonStringEnumConverter());

            var services = await getResponse.Content
                .ReadFromJsonAsync<List<ServiceResponse>>(jsonOptions);

            Assert.NotNull(services);
            Assert.Equal(2, services.Count);

            Assert.Equal(
                new[] { serviceAId, serviceBId },
                services.Select(service => service.Id).ToArray());

            Assert.Equal("Service A", services[0].Name);
            Assert.Equal(100m, services[0].Price);
            Assert.Equal(60, services[0].DurationMinutes);

            Assert.Equal("Service B", services[1].Name);
            Assert.Equal(150m, services[1].Price);
            Assert.Equal(90, services[1].DurationMinutes);

            Assert.All(services, service =>
            {
                Assert.Equal(businessId, service.BusinessId);
                Assert.Equal(ServiceType.Single, service.ServiceType);
                Assert.True(service.IsActive);
                Assert.Empty(service.ComponentServiceIds);
            });

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(link => link.ProfessionalId == professionalId)
                .ToListAsync();

            Assert.Equal(2, links.Count);
            Assert.Contains(links, link => link.ServiceId == serviceAId);
            Assert.Contains(links, link => link.ServiceId == serviceBId);

            Assert.All(links, link =>
            {
                Assert.Equal(tenantId, link.TenantId);
                Assert.True(link.IsActive);
                Assert.Equal(link.CreatedAt, link.UpdatedAt);
            });
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_ShouldDeactivateAndReactivateLinks()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.Add(
                    new Service(
                        serviceId,
                        tenantId,
                        businessId,
                        "Service",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var route =
                $"/api/v1/professionals/{professionalId}/services";

            using var createResponse = await client.PutAsJsonAsync(
                route,
                new { serviceIds = new[] { serviceId } });

            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            DateTimeOffset originalCreatedAt;

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalServices
                    .AsNoTracking()
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.ServiceId == serviceId);

                Assert.True(link.IsActive);
                originalCreatedAt = link.CreatedAt;
            }

            using var deactivateResponse = await client.PutAsJsonAsync(
                route,
                new { serviceIds = Array.Empty<Guid>() });

            Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

            using var emptyResponse = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);

            var jsonOptions = new JsonSerializerOptions(
                JsonSerializerDefaults.Web);

            jsonOptions.Converters.Add(
                new JsonStringEnumConverter());

            var emptyServices = await emptyResponse.Content
                .ReadFromJsonAsync<List<ServiceResponse>>(jsonOptions);

            Assert.NotNull(emptyServices);
            Assert.Empty(emptyServices);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalServices
                    .AsNoTracking()
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.ServiceId == serviceId);

                Assert.False(link.IsActive);
                Assert.Equal(originalCreatedAt, link.CreatedAt);
            }

            using var reactivateResponse = await client.PutAsJsonAsync(
                route,
                new { serviceIds = new[] { serviceId } });

            Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);

            using var finalResponse = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);

            var finalServices = await finalResponse.Content
                .ReadFromJsonAsync<List<ServiceResponse>>(jsonOptions);

            Assert.NotNull(finalServices);

            var service = Assert.Single(finalServices);
            Assert.Equal(serviceId, service.Id);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(x => x.ProfessionalId == professionalId)
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(serviceId, persistedLink.ServiceId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(originalCreatedAt, persistedLink.CreatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    private async Task SeedAsync(
        Guid tenantId,
        Guid businessId,
        Guid professionalId,
        string name)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        context.Tenants.Add(
            new Tenant(tenantId, $"HTTP Tenant {tenantId}"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "HTTP Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Professionals.Add(
            new Professional(
                professionalId,
                tenantId,
                businessId,
                name,
                CreatedAt));

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateProfessional_WithBusinessFromCurrentTenant_ShouldReturnCreated()
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
                    new Tenant(tenantId, "Create Professional Tenant"));

                seedContext.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Create Professional Business",
                        CreatedAt));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId,
                    name = "  Arthur Silva  "
                });

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var professional = await response.Content
                .ReadFromJsonAsync<ProfessionalResponse>();

            Assert.NotNull(professional);
            Assert.NotEqual(Guid.Empty, professional.Id);
            Assert.Equal(businessId, professional.BusinessId);
            Assert.Equal("Arthur Silva", professional.Name);
            Assert.True(professional.IsActive);

            Assert.Equal(
                $"/api/v1/professionals/{professional.Id}",
                response.Headers.Location?.ToString());

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedProfessional = await verificationContext
                .Professionals
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == professional.Id);

            Assert.Equal(tenantId, persistedProfessional.TenantId);
            Assert.Equal(businessId, persistedProfessional.BusinessId);
            Assert.Equal("Arthur Silva", persistedProfessional.Name);
            Assert.True(persistedProfessional.IsActive);

            Assert.Equal(
                persistedProfessional.CreatedAt,
                persistedProfessional.UpdatedAt);

            Assert.True(
                persistedProfessional.CreatedAt > CreatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task CreateProfessional_WithBusinessFromAnotherTenant_ShouldReturnNotFound()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var context = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantAId, "Create Professional Tenant A"));

                await context.SaveChangesAsync();
            }

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId = businessBId,
                    name = "Cross Tenant Professional"
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId));

            // Confirms that the requested Business physically exists.
            var business = await verificationContext.Businesses
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == businessBId);

            Assert.Equal(tenantBId, business.TenantId);

            // Checks both tenants, including any incorrectly inserted row.
            var professionals = await verificationContext.Professionals
                .IgnoreQueryFilters()
                .Where(x =>
                    x.TenantId == tenantAId ||
                    x.TenantId == tenantBId ||
                    x.BusinessId == businessBId)
                .ToListAsync();

            var professional = Assert.Single(professionals);

            Assert.Equal(professionalBId, professional.Id);
            Assert.Equal(tenantBId, professional.TenantId);
            Assert.Equal(businessBId, professional.BusinessId);
            Assert.Equal("Professional B", professional.Name);
            Assert.True(professional.IsActive);
            Assert.Equal(CreatedAt, professional.CreatedAt);
            Assert.Equal(CreatedAt, professional.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task UpdateProfessional_FromCurrentTenant_ShouldReturnOkAndPersistChanges()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional Original");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}",
                new
                {
                    name = "  Professional Atualizado  ",
                    isActive = false
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var professional = await response.Content
                .ReadFromJsonAsync<ProfessionalResponse>();

            Assert.NotNull(professional);
            Assert.Equal(professionalId, professional.Id);
            Assert.Equal(businessId, professional.BusinessId);
            Assert.Equal("Professional Atualizado", professional.Name);
            Assert.False(professional.IsActive);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var persistedProfessional = await verificationContext
                .Professionals
                .SingleAsync(x => x.Id == professionalId);

            Assert.Equal(professionalId, persistedProfessional.Id);
            Assert.Equal(tenantId, persistedProfessional.TenantId);
            Assert.Equal(businessId, persistedProfessional.BusinessId);
            Assert.Equal("Professional Atualizado", persistedProfessional.Name);
            Assert.False(persistedProfessional.IsActive);
            Assert.Equal(CreatedAt, persistedProfessional.CreatedAt);
            Assert.True(persistedProfessional.UpdatedAt > CreatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task UpdateProfessional_FromAnotherTenant_ShouldReturnNotFoundAndPreserveProfessional()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var context = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantAId, "Update Professional Tenant A"));

                await context.SaveChangesAsync();
            }

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional Original");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalBId}",
                new
                {
                    name = "Alteração indevida",
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId));

            var professional = await verificationContext.Professionals
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == professionalBId);

            Assert.Equal(professionalBId, professional.Id);
            Assert.Equal(tenantBId, professional.TenantId);
            Assert.Equal(businessBId, professional.BusinessId);
            Assert.Equal("Professional Original", professional.Name);
            Assert.True(professional.IsActive);
            Assert.Equal(CreatedAt, professional.CreatedAt);
            Assert.Equal(CreatedAt, professional.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task GetProfessionalById_ShouldReturnOnlyToCurrentTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantBId, "Get Professional Tenant B"));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();

            using var ownerClient = factory.CreateClient();
            ownerClient.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var ownerResponse = await ownerClient.GetAsync(
                $"/api/v1/professionals/{professionalAId}");

            Assert.Equal(
                HttpStatusCode.OK,
                ownerResponse.StatusCode);

            var professional = await ownerResponse.Content
                .ReadFromJsonAsync<ProfessionalResponse>();

            Assert.NotNull(professional);
            Assert.Equal(professionalAId, professional.Id);
            Assert.Equal(businessAId, professional.BusinessId);
            Assert.Equal("Professional A", professional.Name);
            Assert.True(professional.IsActive);

            using var otherTenantClient = factory.CreateClient();
            otherTenantClient.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantBId.ToString());

            using var otherTenantResponse = await otherTenantClient.GetAsync(
                $"/api/v1/professionals/{professionalAId}");

            Assert.Equal(
                HttpStatusCode.NotFound,
                otherTenantResponse.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task CreateProfessional_WithUnknownBusiness_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var unknownBusinessId = Guid.NewGuid();

        try
        {
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantId, "Unknown Business Tenant"));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId = unknownBusinessId,
                    name = "Professional Sem Business"
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.Businesses
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.Id == unknownBusinessId));

            Assert.False(
                await verificationContext.Professionals
                    .IgnoreQueryFilters()
                    .AnyAsync(x =>
                        x.TenantId == tenantId ||
                        x.BusinessId == unknownBusinessId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalLocations_WithLocationFromAnotherTenant_ShouldReturnNotFoundAndPreserveLinks()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                context.Locations.Add(
                    new Location(
                        locationAId,
                        tenantAId,
                        businessAId,
                        "Location A",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantAId,
                        professionalAId,
                        locationAId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.Locations.Add(
                    new Location(
                        locationBId,
                        tenantBId,
                        businessBId,
                        "Location B",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantBId,
                        professionalBId,
                        locationBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalAId}/locations",
                new { locationIds = new[] { locationBId } });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId));

            var links = await verificationContext.ProfessionalLocations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(link =>
                    link.TenantId == tenantAId ||
                    link.TenantId == tenantBId)
                .ToListAsync();

            Assert.Equal(2, links.Count);

            var linkA = Assert.Single(
                links,
                link => link.TenantId == tenantAId);

            var linkB = Assert.Single(
                links,
                link => link.TenantId == tenantBId);

            Assert.Equal(professionalAId, linkA.ProfessionalId);
            Assert.Equal(locationAId, linkA.LocationId);
            Assert.Equal(professionalBId, linkB.ProfessionalId);
            Assert.Equal(locationBId, linkB.LocationId);

            Assert.All(links, link =>
            {
                Assert.True(link.IsActive);
                Assert.Equal(CreatedAt, link.CreatedAt);
                Assert.Equal(CreatedAt, link.UpdatedAt);
            });
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task ProfessionalLocations_WithUnknownLocation_ShouldReturnNotFoundAndPreserveLink()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var unknownLocationId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Locations.Add(
                    new Location(
                        locationId,
                        tenantId,
                        businessId,
                        "Location",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/locations",
                new
                {
                    locationIds = new[] { locationId, unknownLocationId }
                });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.Locations
                    .IgnoreQueryFilters()
                    .AnyAsync(location => location.Id == unknownLocationId));

            var link = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(locationId, link.LocationId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_WithUnknownProfessional_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var existingProfessionalId = Guid.NewGuid();
        var unknownProfessionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                existingProfessionalId,
                "Existing Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.Add(
                    new Service(
                        serviceId,
                        tenantId,
                        businessId,
                        "Service",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var route =
                $"/api/v1/professionals/{unknownProfessionalId}/services";

            using var getResponse = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

            using var putResponse = await client.PutAsJsonAsync(
                route,
                new { serviceIds = new[] { serviceId } });

            Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.ProfessionalServices
                    .AnyAsync(x => x.TenantId == tenantId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_WithUnknownService_ShouldReturnNotFoundAndPreserveLinks()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var unknownServiceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.Add(
                    new Service(
                        serviceId,
                        tenantId,
                        businessId,
                        "Service",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/services",
                new
                {
                    serviceIds = new[] { serviceId, unknownServiceId }
                });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(x => x.ProfessionalId == professionalId)
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(serviceId, persistedLink.ServiceId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(CreatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_WithProfessionalFromAnotherTenant_ShouldReturnNotFoundAndPreserveLinks()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                context.Services.Add(
                    new Service(
                        serviceAId,
                        tenantAId,
                        businessAId,
                        "Service A",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.Services.Add(
                    new Service(
                        serviceBId,
                        tenantBId,
                        businessBId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantBId,
                        professionalBId,
                        serviceBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var route =
                $"/api/v1/professionals/{professionalBId}/services";

            using var getResponse = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

            using var putResponse = await client.PutAsJsonAsync(
                route,
                new { serviceIds = new[] { serviceAId } });

            Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId));

            var links = await verificationContext.ProfessionalServices
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == tenantAId ||
                    x.TenantId == tenantBId)
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(tenantBId, persistedLink.TenantId);
            Assert.Equal(professionalBId, persistedLink.ProfessionalId);
            Assert.Equal(serviceBId, persistedLink.ServiceId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(CreatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_WithServiceFromAnotherTenant_ShouldReturnNotFoundAndPreserveLinks()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                context.Services.Add(
                    new Service(
                        serviceAId,
                        tenantAId,
                        businessAId,
                        "Service A",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantAId,
                        professionalAId,
                        serviceAId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.Services.Add(
                    new Service(
                        serviceBId,
                        tenantBId,
                        businessBId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalAId}/services",
                new
                {
                    serviceIds = new[] { serviceAId, serviceBId }
                });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId));

            var links = await verificationContext.ProfessionalServices
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == tenantAId ||
                    x.TenantId == tenantBId)
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(tenantAId, persistedLink.TenantId);
            Assert.Equal(professionalAId, persistedLink.ProfessionalId);
            Assert.Equal(serviceAId, persistedLink.ServiceId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(CreatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_WithServiceFromAnotherBusiness_ShouldReturnConflictAndPreserveLinks()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var anotherBusinessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var anotherServiceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Businesses.Add(new Business(
                    anotherBusinessId,
                    tenantId,
                    BusinessTypeIds.Barbershop,
                    "Another Business",
                    CreatedAt));

                await context.SaveChangesAsync();

                context.Services.AddRange(
                    new Service(
                        serviceId,
                        tenantId,
                        businessId,
                        "Original Service",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt),
                    new Service(
                        anotherServiceId,
                        tenantId,
                        anotherBusinessId,
                        "Another Business Service",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/services",
                new
                {
                    serviceIds = new[]
                    {
                        serviceId,
                        anotherServiceId
                    }
                });

            Assert.Equal(
                HttpStatusCode.Conflict,
                response.StatusCode);

            var problem = await response.Content
                .ReadFromJsonAsync<JsonElement>();

            Assert.Equal(
                409,
                problem.GetProperty("status").GetInt32());

            Assert.Equal(
                "Service does not belong to the professional business",
                problem.GetProperty("title").GetString());

            Assert.Equal(
                "All services must belong to the same Business as the Professional.",
                problem.GetProperty("detail").GetString());

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(link => link.ProfessionalId == professionalId)
                .ToListAsync();

            var link = Assert.Single(links);

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(serviceId, link.ServiceId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServices_WithCombo_ShouldRequireExplicitLinkAndReturnOrderedComponents()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();
        var comboId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.AddRange(
                    new Service(
                        serviceAId,
                        tenantId,
                        businessId,
                        "Service A",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt),
                    new Service(
                        serviceBId,
                        tenantId,
                        businessId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt),
                    new Service(
                        comboId,
                        tenantId,
                        businessId,
                        "Combo",
                        ServiceType.Combo,
                        230m,
                        150,
                        CreatedAt));

                await context.SaveChangesAsync();

                // Insere em ordem inversa para verificar SortOrder no GET.
                context.ServiceComponents.AddRange(
                    new ServiceComponent(
                        tenantId,
                        comboId,
                        serviceBId,
                        1,
                        CreatedAt),
                    new ServiceComponent(
                        tenantId,
                        comboId,
                        serviceAId,
                        0,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var jsonOptions = new JsonSerializerOptions(
                JsonSerializerDefaults.Web);

            jsonOptions.Converters.Add(
                new JsonStringEnumConverter());

            using (var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/services",
                new { serviceIds = new[] { serviceAId, serviceBId } }))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using (var response = await client.GetAsync(
                $"/api/v1/professionals/{professionalId}/services"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var services = await response.Content
                    .ReadFromJsonAsync<List<ServiceResponse>>(jsonOptions);

                Assert.NotNull(services);
                Assert.Equal(2, services.Count);

                Assert.Equal(
                    new[] { serviceAId, serviceBId },
                    services.Select(service => service.Id).ToArray());

                Assert.DoesNotContain(
                    services,
                    service => service.Id == comboId);
            }

            using (var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/services",
                new { serviceIds = new[] { comboId } }))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using (var response = await client.GetAsync(
                $"/api/v1/professionals/{professionalId}/services"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var services = await response.Content
                    .ReadFromJsonAsync<List<ServiceResponse>>(jsonOptions);

                Assert.NotNull(services);

                var combo = Assert.Single(services);

                Assert.Equal(comboId, combo.Id);
                Assert.Equal(businessId, combo.BusinessId);
                Assert.Equal(ServiceType.Combo, combo.ServiceType);

                Assert.Equal(
                    new[] { serviceAId, serviceBId },
                    combo.ComponentServiceIds);
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(link => link.ProfessionalId == professionalId)
                .ToListAsync();

            Assert.Equal(3, links.Count);

            var comboLink = Assert.Single(
                links,
                link => link.ServiceId == comboId);

            Assert.True(comboLink.IsActive);

            Assert.All(
                links.Where(link => link.ServiceId != comboId),
                link => Assert.False(link.IsActive));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalsFilters_WithMultipleServices_ShouldRequireAllServices()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var partialProfessionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Fully Qualified Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Professionals.Add(new Professional(
                    partialProfessionalId,
                    tenantId,
                    businessId,
                    "Partially Qualified Professional",
                    CreatedAt));

                context.Services.AddRange(
                    new Service(
                        serviceAId,
                        tenantId,
                        businessId,
                        "Service A",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt),
                    new Service(
                        serviceBId,
                        tenantId,
                        businessId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalServices.AddRange(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceAId,
                        CreatedAt),
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceBId,
                        CreatedAt),
                    new ProfessionalService(
                        tenantId,
                        partialProfessionalId,
                        serviceAId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            // Ambos possuem o primeiro serviço.
            using (var response = await client.GetAsync(
                $"/api/v1/professionals?serviceIds={serviceAId}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);

                Assert.Equal(
                    new[] { professionalId, partialProfessionalId }
                        .OrderBy(id => id)
                        .ToArray(),
                    professionals
                        .Select(professional => professional.Id)
                        .OrderBy(id => id)
                        .ToArray());
            }

            // Apenas um possui todos os serviços solicitados.
            using (var response = await client.GetAsync(
                $"/api/v1/professionals?serviceIds={serviceAId}&serviceIds={serviceBId}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);

                var professional = Assert.Single(professionals);

                Assert.Equal(professionalId, professional.Id);
                Assert.Equal(businessId, professional.BusinessId);
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalsFilters_WithResourcesFromAnotherTenant_ShouldReturnEmptyList()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.Locations.Add(new Location(
                    locationBId,
                    tenantBId,
                    businessBId,
                    "Location B",
                    "BR",
                    "America/Sao_Paulo",
                    CreatedAt));

                context.Services.Add(new Service(
                    serviceBId,
                    tenantBId,
                    businessBId,
                    "Service B",
                    ServiceType.Single,
                    100m,
                    60,
                    CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantBId,
                        professionalBId,
                        locationBId,
                        CreatedAt));

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantBId,
                        professionalBId,
                        serviceBId,
                        CreatedAt));

                context.LocationServices.Add(
                    new LocationService(
                        tenantBId,
                        locationBId,
                        serviceBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();

            // O proprietário consegue consultar o profissional elegível.
            using (var ownerClient = factory.CreateClient())
            {
                ownerClient.DefaultRequestHeaders.Add(
                    "X-Tenant-Id",
                    tenantBId.ToString());

                using var response = await ownerClient.GetAsync(
                    $"/api/v1/professionals?active=true&locationId={locationBId}&serviceIds={serviceBId}");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);

                Assert.Equal(
                    professionalBId,
                    Assert.Single(professionals).Id);
            }

            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var queries = new[]
            {
                $"locationId={locationBId}",
                $"serviceIds={serviceBId}",
                $"active=true&locationId={locationBId}&serviceIds={serviceBId}"
            };

            foreach (var query in queries)
            {
                using var response = await client.GetAsync(
                    $"/api/v1/professionals?{query}");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);
                Assert.Empty(professionals);
            }
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task ProfessionalsFilters_WithCombo_ShouldRequireExplicitProfessionalLink()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();
        var comboId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.AddRange(
                    new Service(
                        serviceAId,
                        tenantId,
                        businessId,
                        "Service A",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt),
                    new Service(
                        serviceBId,
                        tenantId,
                        businessId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt),
                    new Service(
                        comboId,
                        tenantId,
                        businessId,
                        "Combo",
                        ServiceType.Combo,
                        230m,
                        150,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ServiceComponents.AddRange(
                    new ServiceComponent(
                        tenantId,
                        comboId,
                        serviceAId,
                        0,
                        CreatedAt),
                    new ServiceComponent(
                        tenantId,
                        comboId,
                        serviceBId,
                        1,
                        CreatedAt));

                context.ProfessionalServices.AddRange(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceAId,
                        CreatedAt),
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            // Possuir os componentes não habilita o COMBO.
            using (var response = await client.GetAsync(
                $"/api/v1/professionals?active=true&serviceIds={comboId}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);
                Assert.Empty(professionals);
            }

            // Substitui os componentes pelo vínculo explícito com o COMBO.
            using (var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/services",
                new { serviceIds = new[] { comboId } }))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using (var response = await client.GetAsync(
                $"/api/v1/professionals?active=true&serviceIds={comboId}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);

                Assert.Equal(
                    professionalId,
                    Assert.Single(professionals).Id);
            }

            // Habilitação para COMBO também não habilita seus componentes.
            using (var response = await client.GetAsync(
                $"/api/v1/professionals?active=true&serviceIds={serviceAId}&serviceIds={serviceBId}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);
                Assert.Empty(professionals);
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProfessionalsFilters_WithLocationAndServices_ShouldRequireAllServicesAtLocation(
        bool allServicesAtLocation)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Locations.Add(new Location(
                    locationId,
                    tenantId,
                    businessId,
                    "Location",
                    "BR",
                    "America/Sao_Paulo",
                    CreatedAt));

                context.Services.AddRange(
                    new Service(
                        serviceAId,
                        tenantId,
                        businessId,
                        "Service A",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt),
                    new Service(
                        serviceBId,
                        tenantId,
                        businessId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                context.ProfessionalServices.AddRange(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceAId,
                        CreatedAt),
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceBId,
                        CreatedAt));

                context.LocationServices.Add(
                    new LocationService(
                        tenantId,
                        locationId,
                        serviceAId,
                        CreatedAt));

                if (allServicesAtLocation)
                {
                    context.LocationServices.Add(
                        new LocationService(
                            tenantId,
                            locationId,
                            serviceBId,
                            CreatedAt));
                }

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            // Sem Location, o profissional possui os dois serviços.
            using (var response = await client.GetAsync(
                $"/api/v1/professionals?active=true&serviceIds={serviceAId}&serviceIds={serviceBId}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);

                Assert.Equal(
                    professionalId,
                    Assert.Single(professionals).Id);
            }

            // Com Location, ambos também precisam estar disponíveis nela.
            using (var response = await client.GetAsync(
                $"/api/v1/professionals?active=true&locationId={locationId}&serviceIds={serviceAId}&serviceIds={serviceBId}"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var professionals = await response.Content
                    .ReadFromJsonAsync<List<ProfessionalResponse>>();

                Assert.NotNull(professionals);

                if (allServicesAtLocation)
                {
                    Assert.Equal(
                        professionalId,
                        Assert.Single(professionals).Id);
                }
                else
                {
                    Assert.Empty(professionals);
                }
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ProfessionalsFilters_WithService_ShouldRequireActiveServiceAndLink(
        bool serviceActive,
        bool linkActive)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var service = new Service(
                    serviceId,
                    tenantId,
                    businessId,
                    "Service",
                    ServiceType.Single,
                    100m,
                    60,
                    CreatedAt);

                context.Services.Add(service);

                await context.SaveChangesAsync();

                service.Update(
                    "Service",
                    100m,
                    60,
                    serviceActive,
                    CreatedAt.AddHours(1));

                var link = new ProfessionalService(
                    tenantId,
                    professionalId,
                    serviceId,
                    CreatedAt);

                if (!linkActive)
                {
                    link.Update(false, CreatedAt.AddHours(1));
                }

                context.ProfessionalServices.Add(link);

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.GetAsync(
                $"/api/v1/professionals?active=true&serviceIds={serviceId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var professionals = await response.Content
                .ReadFromJsonAsync<List<ProfessionalResponse>>();

            Assert.NotNull(professionals);

            if (serviceActive && linkActive)
            {
                var professional = Assert.Single(professionals);

                Assert.Equal(professionalId, professional.Id);
                Assert.Equal(businessId, professional.BusinessId);
                Assert.True(professional.IsActive);
            }
            else
            {
                Assert.Empty(professionals);
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProfessionalsFilters_WithActive_ShouldReturnOnlyMatchingProfessionals(
        bool active)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var activeProfessionalId = Guid.NewGuid();
        var inactiveProfessionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                activeProfessionalId,
                "Active Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var inactiveProfessional = new Professional(
                    inactiveProfessionalId,
                    tenantId,
                    businessId,
                    "Inactive Professional",
                    CreatedAt);

                inactiveProfessional.Update(
                    "Inactive Professional",
                    false,
                    CreatedAt.AddHours(1));

                context.Professionals.Add(inactiveProfessional);

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.GetAsync(
                $"/api/v1/professionals?active={active.ToString().ToLowerInvariant()}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var professionals = await response.Content
                .ReadFromJsonAsync<List<ProfessionalResponse>>();

            Assert.NotNull(professionals);

            var professional = Assert.Single(professionals);

            Assert.Equal(
                active ? activeProfessionalId : inactiveProfessionalId,
                professional.Id);

            Assert.Equal(active, professional.IsActive);
            Assert.Equal(businessId, professional.BusinessId);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ProfessionalsFilters_WithLocation_ShouldRequireActiveLocationAndLink(
        bool locationActive,
        bool linkActive)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var location = new Location(
                    locationId,
                    tenantId,
                    businessId,
                    "Location",
                    "BR",
                    "America/Sao_Paulo",
                    CreatedAt);

                context.Locations.Add(location);

                await context.SaveChangesAsync();

                // Altera pelo EF para não depender da assinatura de Location.Update.
                context.Entry(location)
                    .Property(entity => entity.IsActive)
                    .CurrentValue = locationActive;

                var link = new ProfessionalLocation(
                    tenantId,
                    professionalId,
                    locationId,
                    CreatedAt);

                if (!linkActive)
                {
                    link.Update(false, CreatedAt.AddHours(1));
                }

                context.ProfessionalLocations.Add(link);

                // Profissional do mesmo Business, mas sem vínculo com a Location.
                context.Professionals.Add(new Professional(
                    Guid.NewGuid(),
                    tenantId,
                    businessId,
                    "Unlinked Professional",
                    CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.GetAsync(
                $"/api/v1/professionals?locationId={locationId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var professionals = await response.Content
                .ReadFromJsonAsync<List<ProfessionalResponse>>();

            Assert.NotNull(professionals);

            if (locationActive && linkActive)
            {
                var professional = Assert.Single(professionals);

                Assert.Equal(professionalId, professional.Id);
                Assert.Equal(businessId, professional.BusinessId);
            }
            else
            {
                Assert.Empty(professionals);
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task ProfessionalLocations_WithUnknownProfessional_ShouldReturnNotFound(
        string method)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var existingProfessionalId = Guid.NewGuid();
        var unknownProfessionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                existingProfessionalId,
                "Existing Professional");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var url =
                $"/api/v1/professionals/{unknownProfessionalId}/locations";

            using var response = method == "GET"
                ? await client.GetAsync(url)
                : await client.PutAsJsonAsync(
                    url,
                    new { locationIds = Array.Empty<Guid>() });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.ProfessionalLocations.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Professional_WithUnknownId_ShouldReturnNotFound(
        string method)
    {
        var tenantId = Guid.NewGuid();

        try
        {
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantId, "Unknown Professional Tenant"));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var url = $"/api/v1/professionals/{Guid.NewGuid()}";

            using var response = method == "GET"
                ? await client.GetAsync(url)
                : await client.PutAsJsonAsync(
                    url,
                    new
                    {
                        name = "Professional Inexistente",
                        isActive = false
                    });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.Professionals.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(null, "TENANT_HEADER_REQUIRED")]
    [InlineData("tenant-invalido", "INVALID_TENANT_ID")]
    public async Task GetProfessionals_WithMissingOrInvalidTenantHeader_ShouldReturnBadRequest(
        string? tenantHeader,
        string expectedCode)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        if (tenantHeader is not null)
        {
            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantHeader);
        }

        using var response = await client.GetAsync(
            "/api/v1/professionals");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(expectedCode, body);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task ProfessionalLocations_WithProfessionalFromAnotherTenant_ShouldReturnNotFoundAndPreserveLink(
        string method)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.Locations.Add(
                    new Location(
                        locationBId,
                        tenantBId,
                        businessBId,
                        "Location B",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantBId,
                        professionalBId,
                        locationBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            var url =
                $"/api/v1/professionals/{professionalBId}/locations";

            using var response = method == "GET"
                ? await client.GetAsync(url)
                : await client.PutAsJsonAsync(
                    url,
                    new { locationIds = Array.Empty<Guid>() });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId));

            var links = await verificationContext.ProfessionalLocations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(link =>
                    link.TenantId == tenantAId ||
                    link.TenantId == tenantBId)
                .ToListAsync();

            var link = Assert.Single(links);

            Assert.Equal(tenantBId, link.TenantId);
            Assert.Equal(professionalBId, link.ProfessionalId);
            Assert.Equal(locationBId, link.LocationId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Theory]
    [InlineData(true, 10)]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(false, 161)]
    public async Task CreateProfessional_WithInvalidRequest_ShouldReturnBadRequestAndNotPersist(
        bool emptyBusinessId,
        int nameLength)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var name = nameLength <= 3
            ? new string(' ', nameLength)
            : new string('A', nameLength);

        try
        {
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantId, "Invalid Create Tenant"));

                context.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Invalid Create Business",
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId = emptyBusinessId
                        ? Guid.Empty
                        : businessId,
                    name
                });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.Professionals
                    .IgnoreQueryFilters()
                    .AnyAsync(x =>
                        x.TenantId == tenantId ||
                        x.BusinessId == businessId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(161)]
    public async Task UpdateProfessional_WithInvalidName_ShouldReturnBadRequestAndPreserveProfessional(
        int nameLength)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        var invalidName = nameLength == 161
            ? new string('A', nameLength)
            : new string(' ', nameLength);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional Original");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}",
                new
                {
                    name = invalidName,
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var professional = await verificationContext.Professionals
                .SingleAsync(x => x.Id == professionalId);

            Assert.Equal(professionalId, professional.Id);
            Assert.Equal(tenantId, professional.TenantId);
            Assert.Equal(businessId, professional.BusinessId);
            Assert.Equal("Professional Original", professional.Name);
            Assert.True(professional.IsActive);
            Assert.Equal(CreatedAt, professional.CreatedAt);
            Assert.Equal(CreatedAt, professional.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("emptyGuid")]
    [InlineData("duplicate")]
    public async Task ProfessionalServices_WithInvalidPayload_ShouldReturnBadRequestAndPreserveLinks(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.Add(
                    new Service(
                        serviceId,
                        tenantId,
                        businessId,
                        "Service",
                        ServiceType.Single,
                        100m,
                        60,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            object payload = scenario switch
            {
                "missing" => new { },
                "null" => new { serviceIds = (Guid[]?)null },
                "emptyGuid" => new { serviceIds = new[] { Guid.Empty } },
                "duplicate" => new
                {
                    serviceIds = new[] { serviceId, serviceId }
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}/services",
                payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await response.Content
                .ReadFromJsonAsync<JsonElement>();

            Assert.Equal(
                "Invalid professional services request",
                problem.GetProperty("title").GetString());

            Assert.Equal(
                400,
                problem.GetProperty("status").GetInt32());

            var expectedDetail = scenario == "duplicate"
                ? "ServiceIds must not contain duplicates."
                : "ServiceIds is required and must not contain empty GUIDs.";

            Assert.Equal(
                expectedDetail,
                problem.GetProperty("detail").GetString());

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(x => x.ProfessionalId == professionalId)
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(tenantId, persistedLink.TenantId);
            Assert.Equal(serviceId, persistedLink.ServiceId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(CreatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("active=invalid")]
    [InlineData("locationId=invalid")]
    [InlineData("locationId=00000000-0000-0000-0000-000000000000")]
    [InlineData("serviceIds=invalid")]
    [InlineData("serviceIds=00000000-0000-0000-0000-000000000000")]
    [InlineData(
        "serviceIds=11111111-1111-4111-8111-111111111111" +
        "&serviceIds=11111111-1111-4111-8111-111111111111")]
    public async Task ProfessionalsFilters_WithInvalidQuery_ShouldReturnBadRequest(
        string queryString)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.GetAsync(
                $"/api/v1/professionals?{queryString}");

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }
    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        var professionalServiceLinks = await context.ProfessionalServices
            .Where(link => link.TenantId == tenantId)
            .ToListAsync();

        context.ProfessionalServices.RemoveRange(professionalServiceLinks);
        await context.SaveChangesAsync();

        var professionalLocationLinks = await context.ProfessionalLocations
            .Where(link => link.TenantId == tenantId)
            .ToListAsync();

        context.ProfessionalLocations.RemoveRange(professionalLocationLinks);
        await context.SaveChangesAsync();

        var locationServiceLinks = await context.LocationServices
            .Where(link => link.TenantId == tenantId)
            .ToListAsync();

        context.LocationServices.RemoveRange(locationServiceLinks);
        await context.SaveChangesAsync();

        var components = await context.ServiceComponents
            .Where(component => component.TenantId == tenantId)
            .ToListAsync();

        context.ServiceComponents.RemoveRange(components);
        await context.SaveChangesAsync();

        var professionals = await context.Professionals
            .Where(professional => professional.TenantId == tenantId)
            .ToListAsync();

        context.Professionals.RemoveRange(professionals);
        await context.SaveChangesAsync();

        var locations = await context.Locations
            .Where(location => location.TenantId == tenantId)
            .ToListAsync();

        context.Locations.RemoveRange(locations);
        await context.SaveChangesAsync();

        var services = await context.Services
            .Where(service => service.TenantId == tenantId)
            .ToListAsync();

        context.Services.RemoveRange(services);
        await context.SaveChangesAsync();

        var businesses = await context.Businesses
            .Where(business => business.TenantId == tenantId)
            .ToListAsync();

        context.Businesses.RemoveRange(businesses);
        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(tenant => tenant.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }
    private DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
    }

    private static TenantContext CreateTenantContext(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return tenantContext;
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
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<AppDbContext>();

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_fixture.ConnectionString));
                });
            });
    }
}