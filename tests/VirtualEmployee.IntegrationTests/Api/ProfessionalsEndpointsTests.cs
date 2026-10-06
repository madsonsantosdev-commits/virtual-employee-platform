using System.Net;
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

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        var links = await context.ProfessionalLocations
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.ProfessionalLocations.RemoveRange(links);
        await context.SaveChangesAsync();

        var professionals = await context.Professionals
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Professionals.RemoveRange(professionals);
        await context.SaveChangesAsync();

        var locations = await context.Locations
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Locations.RemoveRange(locations);
        await context.SaveChangesAsync();

        var businesses = await context.Businesses
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Businesses.RemoveRange(businesses);
        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(x => x.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
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