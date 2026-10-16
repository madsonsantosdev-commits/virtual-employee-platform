using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.BookingQuotes.GetBookingQuote;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class BookingQuotesEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public BookingQuotesEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task BookingQuote_ShouldCalculateTotalsAndPreserveRequestedOrder()
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

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceBId, serviceAId }
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var quote = await response.Content
                .ReadFromJsonAsync<BookingQuoteResponse>();

            Assert.NotNull(quote);
            Assert.Equal(locationId, quote.LocationId);
            Assert.Equal(80m, quote.TotalPrice);
            Assert.Equal(65, quote.TotalDurationMinutes);

            Assert.Collection(
                quote.Items,
                item =>
                {
                    Assert.Equal(serviceBId, item.ServiceId);
                    Assert.Equal("Barba", item.Name);
                    Assert.Equal(30m, item.Price);
                    Assert.Equal(20, item.DurationMinutes);
                },
                item =>
                {
                    Assert.Equal(serviceAId, item.ServiceId);
                    Assert.Equal("Corte", item.Name);
                    Assert.Equal(50m, item.Price);
                    Assert.Equal(45, item.DurationMinutes);
                });

            await using var context = CreateContext(tenantId);

            var services = await context.Services
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(2, services.Count);

            Assert.All(services, service =>
            {
                Assert.Equal(CreatedAt, service.CreatedAt);
                Assert.Equal(CreatedAt, service.UpdatedAt);
            });
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task BookingQuote_WithServiceFromAnotherBusiness_ShouldReturnConflict()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();
        var otherServiceId = Guid.NewGuid();

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
                context.Businesses.Add(
                    new Business(
                        otherBusinessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Other Quote Business",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.Services.Add(
                    new Service(
                        otherServiceId,
                        tenantId,
                        otherBusinessId,
                        "Other Service",
                        ServiceType.Single,
                        40m,
                        30,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, otherServiceId }
                });

            Assert.Equal(
                HttpStatusCode.Conflict,
                response.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task BookingQuote_WithCombo_ShouldUseOwnValuesAndRecalculate()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();
        var comboId = Guid.NewGuid();

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
                context.Services.Add(
                    new Service(
                        comboId,
                        tenantId,
                        businessId,
                        "Corte + Barba",
                        ServiceType.Combo,
                        70m,
                        60,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.LocationServices.Add(
                    new LocationService(
                        tenantId,
                        locationId,
                        comboId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var payload = new
            {
                locationId,
                serviceIds = new[] { comboId }
            };

            using var firstResponse = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                payload);

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var firstQuote = await firstResponse.Content
                .ReadFromJsonAsync<BookingQuoteResponse>();

            Assert.NotNull(firstQuote);
            Assert.Equal(comboId, Assert.Single(firstQuote.Items).ServiceId);
            Assert.Equal(70m, firstQuote.TotalPrice);
            Assert.Equal(60, firstQuote.TotalDurationMinutes);

            await using (var context = CreateContext(tenantId))
            {
                var combo = await context.Services
                    .SingleAsync(service => service.Id == comboId);

                combo.Update(
                    combo.Name,
                    75m,
                    55,
                    true,
                    CreatedAt.AddHours(1));

                await context.SaveChangesAsync();
            }

            using var secondResponse = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                payload);

            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

            var secondQuote = await secondResponse.Content
                .ReadFromJsonAsync<BookingQuoteResponse>();

            Assert.NotNull(secondQuote);

            var item = Assert.Single(secondQuote.Items);

            Assert.Equal(comboId, item.ServiceId);
            Assert.Equal(75m, item.Price);
            Assert.Equal(55, item.DurationMinutes);
            Assert.Equal(75m, secondQuote.TotalPrice);
            Assert.Equal(55, secondQuote.TotalDurationMinutes);

            // A resposta anterior preserva os valores observados na consulta.
            Assert.Equal(70m, firstQuote.TotalPrice);
            Assert.Equal(60, firstQuote.TotalDurationMinutes);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("missing-location")]
    [InlineData("empty-location")]
    [InlineData("malformed-location")]
    [InlineData("missing-services")]
    [InlineData("null-services")]
    [InlineData("empty-services")]
    [InlineData("empty-service-id")]
    [InlineData("duplicate-services")]
    [InlineData("malformed-service-id")]
    public async Task BookingQuote_WithInvalidPayload_ShouldReturnBadRequest(
        string scenario)
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

            var payload = new Dictionary<string, object?>
            {
                ["locationId"] = locationId,
                ["serviceIds"] = new[] { serviceAId, serviceBId }
            };

            switch (scenario)
            {
                case "missing-location":
                    payload.Remove("locationId");
                    break;

                case "empty-location":
                    payload["locationId"] = Guid.Empty;
                    break;

                case "malformed-location":
                    payload["locationId"] = "invalid-guid";
                    break;

                case "missing-services":
                    payload.Remove("serviceIds");
                    break;

                case "null-services":
                    payload["serviceIds"] = null;
                    break;

                case "empty-services":
                    payload["serviceIds"] = Array.Empty<Guid>();
                    break;

                case "empty-service-id":
                    payload["serviceIds"] = new[] { Guid.Empty };
                    break;

                case "duplicate-services":
                    payload["serviceIds"] =
                        new[] { serviceAId, serviceAId };
                    break;

                case "malformed-service-id":
                    payload["serviceIds"] = new[] { "invalid-guid" };
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                payload);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("missing-location", HttpStatusCode.NotFound)]
    [InlineData("missing-service", HttpStatusCode.NotFound)]
    [InlineData("inactive-location", HttpStatusCode.Conflict)]
    [InlineData("inactive-business", HttpStatusCode.Conflict)]
    [InlineData("inactive-service", HttpStatusCode.Conflict)]
    [InlineData("service-not-offered", HttpStatusCode.Conflict)]
    public async Task BookingQuote_WithUnavailableResources_ShouldReject(
        string scenario,
        HttpStatusCode expectedStatus)
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
                switch (scenario)
                {
                    case "inactive-location":
                    {
                        var location = await context.Locations
                            .SingleAsync(entity => entity.Id == locationId);

                        location.Update(
                            location.Name,
                            location.CountryCode,
                            location.Timezone,
                            false,
                            CreatedAt.AddHours(1));
                        break;
                    }

                    case "inactive-business":
                    {
                        var business = await context.Businesses
                            .SingleAsync(entity => entity.Id == businessId);

                        business.Update(
                            business.BusinessTypeId,
                            business.Name,
                            business.SlotIntervalMinutes,
                            false,
                            CreatedAt.AddHours(1));
                        break;
                    }

                    case "inactive-service":
                    {
                        var service = await context.Services
                            .SingleAsync(entity => entity.Id == serviceBId);

                        service.Update(
                            service.Name,
                            service.Price,
                            service.DurationMinutes,
                            false,
                            CreatedAt.AddHours(1));
                        break;
                    }

                    case "service-not-offered":
                    {
                        var link = await context.LocationServices
                            .SingleAsync(entity =>
                                entity.LocationId == locationId &&
                                entity.ServiceId == serviceBId);

                        context.LocationServices.Remove(link);
                        break;
                    }
                }

                await context.SaveChangesAsync();
            }

            var requestedLocationId = scenario == "missing-location"
                ? Guid.NewGuid()
                : locationId;

            var requestedServiceBId = scenario == "missing-service"
                ? Guid.NewGuid()
                : serviceBId;

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                new
                {
                    locationId = requestedLocationId,
                    serviceIds = new[] { serviceAId, requestedServiceBId }
                });

            Assert.Equal(expectedStatus, response.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("foreign-location")]
    [InlineData("foreign-service")]
    public async Task BookingQuote_WithResourcesFromAnotherTenant_ShouldReturnNotFound(
        string scenario)
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

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            // Confirma primeiro que a cotação do próprio tenant funciona.
            using var ownResponse = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                new
                {
                    locationId = locationAId,
                    serviceIds = new[] { serviceA1Id, serviceA2Id }
                });

            Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

            var requestedLocationId = scenario == "foreign-location"
                ? locationBId
                : locationAId;

            var requestedServiceIds = scenario == "foreign-location"
                ? new[] { serviceB1Id, serviceB2Id }
                : new[] { serviceA1Id, serviceB1Id };

            using var foreignResponse = await client.PostAsJsonAsync(
                "/api/v1/booking/quote",
                new
                {
                    locationId = requestedLocationId,
                    serviceIds = requestedServiceIds
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                foreignResponse.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    #endregion

    #region Helpers

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
            new Tenant(tenantId, "Quote HTTP Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Quote HTTP Business",
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

        context.LocationServices.AddRange(
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

    #endregion
}