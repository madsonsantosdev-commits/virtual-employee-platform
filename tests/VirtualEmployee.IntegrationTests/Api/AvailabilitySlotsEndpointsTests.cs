using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;

using Xunit;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class AvailabilitySlotsEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public AvailabilitySlotsEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SearchSlots_WithValidServices_ShouldReturnOrderedUtcSlots()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19",
                    professionalId
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(result);
            Assert.Equal(locationId, result.LocationId);
            Assert.Equal(new DateOnly(2026, 10, 19), result.Date);
            Assert.Equal("America/Sao_Paulo", result.Timezone);
            Assert.Equal(60, result.TotalDurationMinutes);
            Assert.Equal(7, result.Slots.Count);

            for (var index = 0; index < result.Slots.Count; index++)
            {
                var slot = result.Slots[index];

                var expectedStart = new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
                    .AddMinutes(index * 20);

                Assert.Equal(professionalId, slot.ProfessionalId);
                Assert.Equal(expectedStart, slot.StartsAt);
                Assert.Equal(expectedStart.AddHours(1), slot.EndsAt);
                Assert.Equal(TimeSpan.Zero, slot.StartsAt.Offset);
                Assert.Equal(TimeSpan.Zero, slot.EndsAt.Offset);
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task SearchSlots_FromAnotherTenant_ShouldReturnNotFound()
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                otherTenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19",
                    professionalId
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            // Confirma que a mesma busca funciona para o proprietário.
            using var ownerClient = factory.CreateClient();

            ownerClient.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                ownerTenantId.ToString());

            using var ownerResponse = await ownerClient.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19",
                    professionalId
                });

            Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

            var result = await ownerResponse.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(result);
            Assert.Equal(7, result.Slots.Count);
            Assert.All(
                result.Slots,
                slot => Assert.Equal(professionalId, slot.ProfessionalId));
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Fact]
    public async Task SearchSlots_WithoutProfessional_ShouldReturnAllEligibleProfessionals()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var secondProfessionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            await using (var context = CreateContext(tenantId))
            {
                context.Professionals.Add(
                    new Professional(
                        secondProfessionalId,
                        tenantId,
                        businessId,
                        "Second Professional",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        secondProfessionalId,
                        locationId,
                        CreatedAt));

                context.ProfessionalServices.AddRange(
                    new ProfessionalService(
                        tenantId, secondProfessionalId,
                        serviceAId, CreatedAt),
                    new ProfessionalService(
                        tenantId, secondProfessionalId,
                        serviceBId, CreatedAt));

                await context.SaveChangesAsync();

                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        Guid.NewGuid(),
                        tenantId,
                        locationId,
                        secondProfessionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(12, 0),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19"
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(result);
            Assert.Equal(14, result.Slots.Count);

            Assert.Equal(
                7,
                result.Slots.Count(slot =>
                    slot.ProfessionalId == professionalId));

            Assert.Equal(
                7,
                result.Slots.Count(slot =>
                    slot.ProfessionalId == secondProfessionalId));

            var expectedOrder = result.Slots
                .OrderBy(slot => slot.StartsAt)
                .ThenBy(slot => slot.ProfessionalId)
                .ToArray();

            Assert.Equal(expectedOrder, result.Slots.ToArray());

            Assert.All(result.Slots, slot =>
                Assert.Equal(
                    TimeSpan.FromMinutes(60),
                    slot.EndsAt - slot.StartsAt));

            // Selecionar um profissional restringe o resultado.
            using var selectedResponse = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19",
                    professionalId = secondProfessionalId
                });

            Assert.Equal(HttpStatusCode.OK, selectedResponse.StatusCode);

            var selectedResult = await selectedResponse.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(selectedResult);
            Assert.Equal(7, selectedResult.Slots.Count);
            Assert.All(selectedResult.Slots, slot =>
                Assert.Equal(secondProfessionalId, slot.ProfessionalId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(
        "{\"locationId\":null,\"serviceIds\":[\"11111111-1111-4111-8111-111111111111\"],\"date\":\"2026-10-19\"}")]
    [InlineData(
        "{\"locationId\":\"00000000-0000-0000-0000-000000000000\",\"serviceIds\":[\"11111111-1111-4111-8111-111111111111\"],\"date\":\"2026-10-19\"}")]
    [InlineData(
        "{\"locationId\":\"22222222-2222-4222-8222-222222222222\",\"serviceIds\":null,\"date\":\"2026-10-19\"}")]
    [InlineData(
        "{\"locationId\":\"22222222-2222-4222-8222-222222222222\",\"serviceIds\":[],\"date\":\"2026-10-19\"}")]
    [InlineData(
        "{\"locationId\":\"22222222-2222-4222-8222-222222222222\",\"serviceIds\":[\"00000000-0000-0000-0000-000000000000\"],\"date\":\"2026-10-19\"}")]
    [InlineData(
        "{\"locationId\":\"22222222-2222-4222-8222-222222222222\",\"serviceIds\":[\"11111111-1111-4111-8111-111111111111\",\"11111111-1111-4111-8111-111111111111\"],\"date\":\"2026-10-19\"}")]
    [InlineData(
        "{\"locationId\":\"22222222-2222-4222-8222-222222222222\",\"serviceIds\":[\"11111111-1111-4111-8111-111111111111\"]}")]
    [InlineData(
        "{\"locationId\":\"22222222-2222-4222-8222-222222222222\",\"serviceIds\":[\"11111111-1111-4111-8111-111111111111\"],\"date\":\"2026-10-19\",\"professionalId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData(
        "{\"locationId\":\"invalid-guid\",\"serviceIds\":[],\"date\":\"2026-10-19\"}")]
    [InlineData(
        "{\"locationId\":\"22222222-2222-4222-8222-222222222222\",\"serviceIds\":[\"11111111-1111-4111-8111-111111111111\"],\"date\":\"invalid-date\"}")]
    public async Task SearchSlots_WithInvalidPayload_ShouldReturnBadRequest(
        string payload)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add(
            "X-Tenant-Id",
            Guid.NewGuid().ToString());

        using var content = new StringContent(
            payload,
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            "/api/v1/availability/slots/search",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("inactive-professional")]
    [InlineData("inactive-location-link")]
    [InlineData("inactive-service-link")]
    [InlineData("missing-service-link")]
    public async Task SearchSlots_WithIneligibleProfessional_ShouldReturnEmptySlots(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            await using (var context = CreateContext(tenantId))
            {
                var updatedAt = CreatedAt.AddHours(1);

                switch (scenario)
                {
                    case "inactive-professional":
                        var professional = await context.Professionals
                            .SingleAsync(x => x.Id == professionalId);

                        professional.Update(
                            professional.Name,
                            false,
                            updatedAt);
                        break;

                    case "inactive-location-link":
                        var locationLink = await context.ProfessionalLocations
                            .SingleAsync(x =>
                                x.ProfessionalId == professionalId &&
                                x.LocationId == locationId);

                        locationLink.Update(false, updatedAt);
                        break;

                    case "inactive-service-link":
                        var serviceLink = await context.ProfessionalServices
                            .SingleAsync(x =>
                                x.ProfessionalId == professionalId &&
                                x.ServiceId == serviceBId);

                        serviceLink.Update(false, updatedAt);
                        break;

                    case "missing-service-link":
                        var missingLink = await context.ProfessionalServices
                            .SingleAsync(x =>
                                x.ProfessionalId == professionalId &&
                                x.ServiceId == serviceBId);

                        context.ProfessionalServices.Remove(missingLink);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(scenario));
                }

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19",
                    professionalId
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(result);
            Assert.Equal(locationId, result.LocationId);
            Assert.Equal(60, result.TotalDurationMinutes);
            Assert.Empty(result.Slots);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("inactive-location")]
    [InlineData("inactive-business")]
    [InlineData("inactive-service")]
    [InlineData("service-not-offered")]
    public async Task SearchSlots_WithUnavailableResource_ShouldReturnConflict(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            await using (var context = CreateContext(tenantId))
            {
                var updatedAt = CreatedAt.AddHours(1);

                switch (scenario)
                {
                    case "inactive-location":
                        var location = await context.Locations
                            .SingleAsync(x => x.Id == locationId);

                        location.Update(
                            location.Name,
                            location.CountryCode,
                            location.Timezone,
                            false,
                            updatedAt);
                        break;

                    case "inactive-business":
                        var business = await context.Businesses
                            .SingleAsync(x => x.Id == businessId);

                        business.Update(
                            business.BusinessTypeId,
                            business.Name,
                            business.SlotIntervalMinutes,
                            false,
                            updatedAt);
                        break;

                    case "inactive-service":
                        var service = await context.Services
                            .SingleAsync(x => x.Id == serviceBId);

                        service.Update(
                            service.Name,
                            service.Price,
                            service.DurationMinutes,
                            false,
                            updatedAt);
                        break;

                    case "service-not-offered":
                        var link = await context.LocationServices
                            .SingleAsync(x =>
                                x.LocationId == locationId &&
                                x.ServiceId == serviceBId);

                        context.LocationServices.Remove(link);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(scenario));
                }

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19",
                    professionalId
                });

            Assert.Equal(
                HttpStatusCode.Conflict,
                response.StatusCode);

            using var problem = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            Assert.Equal(
                "Available slots conflict",
                problem.RootElement.GetProperty("title").GetString());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchSlots_WithCombo_ShouldRequireExplicitLinkAndUseComboDuration(
        bool explicitlyEnabled)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();
        var comboId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            await using (var context = CreateContext(tenantId))
            {
                // Componentes somam 60 minutos; o combo possui duração própria de 90.
                context.Services.Add(
                    new Service(
                        comboId,
                        tenantId,
                        businessId,
                        "Combo",
                        ServiceType.Combo,
                        80m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ServiceComponents.AddRange(
                    new ServiceComponent(
                        tenantId, comboId, serviceAId, 0, CreatedAt),
                    new ServiceComponent(
                        tenantId, comboId, serviceBId, 1, CreatedAt));

                context.LocationServices.Add(
                    new LocationService(
                        tenantId, locationId, comboId, CreatedAt));

                if (explicitlyEnabled)
                {
                    context.ProfessionalServices.Add(
                        new ProfessionalService(
                            tenantId, professionalId, comboId, CreatedAt));
                }

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { comboId },
                    date = "2026-10-19",
                    professionalId
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(result);
            Assert.Equal(90, result.TotalDurationMinutes);

            if (!explicitlyEnabled)
            {
                Assert.Empty(result.Slots);
            }
            else
            {
                Assert.Equal(5, result.Slots.Count);

                for (var index = 0; index < result.Slots.Count; index++)
                {
                    var slot = result.Slots[index];

                    var expectedStart = new DateTimeOffset(
                        2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
                        .AddMinutes(index * 20);

                    Assert.Equal(professionalId, slot.ProfessionalId);
                    Assert.Equal(expectedStart, slot.StartsAt);
                    Assert.Equal(expectedStart.AddMinutes(90), slot.EndsAt);
                }
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("day-without-rule")]
    [InlineData("inactive-rule")]
    [InlineData("past-date")]
    public async Task SearchSlots_WithNoAvailableAgenda_ShouldReturnEmptySlots(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            var date = scenario switch
            {
                "day-without-rule" => "2026-10-20",
                "past-date" => "2026-10-12",
                _ => "2026-10-19"
            };

            if (scenario == "inactive-rule")
            {
                await using var context = CreateContext(tenantId);

                var rule = await context.AvailabilityRules
                    .SingleAsync(x =>
                        x.LocationId == locationId &&
                        x.ProfessionalId == professionalId);

                rule.Update(
                    rule.DayOfWeek,
                    rule.StartTime,
                    rule.EndTime,
                    false,
                    CreatedAt.AddHours(1));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date,
                    professionalId
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(result);
            Assert.Equal(60, result.TotalDurationMinutes);
            Assert.Empty(result.Slots);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchSlots_WithScheduleBlock_ShouldExcludeOverlappingSlots(
        bool professionalScoped)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId,
                serviceAId,
                serviceBId);

            await using (var context = CreateContext(tenantId))
            {
                // 10h–10h30 em São Paulo corresponde a 13h–13h30 UTC.
                context.ScheduleBlocks.Add(
                    new ScheduleBlock(
                        Guid.NewGuid(),
                        tenantId,
                        locationId,
                        professionalScoped ? professionalId : null,
                        new DateTimeOffset(
                            2026, 10, 19, 13, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(
                            2026, 10, 19, 13, 30, 0, TimeSpan.Zero),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                new
                {
                    locationId,
                    serviceIds = new[] { serviceAId, serviceBId },
                    date = "2026-10-19",
                    professionalId
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchAvailableSlotsResponse>();

            Assert.NotNull(result);
            Assert.Equal(60, result.TotalDurationMinutes);
            Assert.Equal(3, result.Slots.Count);

            Assert.Equal(
                new[]
                {
                    new DateTimeOffset(
                        2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026, 10, 19, 13, 40, 0, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026, 10, 19, 14, 0, 0, TimeSpan.Zero)
                },
                result.Slots.Select(slot => slot.StartsAt).ToArray());

            Assert.All(result.Slots, slot =>
            {
                Assert.Equal(professionalId, slot.ProfessionalId);
                Assert.Equal(
                    TimeSpan.FromHours(1),
                    slot.EndsAt - slot.StartsAt);
            });
        }
        finally
        {
            await CleanupAsync(tenantId);
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
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<AppDbContext>();
                    services.RemoveAll<TimeProvider>();

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_fixture.ConnectionString));

                    services.AddSingleton<TimeProvider>(
                        new FixedTimeProvider(CreatedAt));
                });
            });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
        Guid professionalId,
        Guid serviceAId,
        Guid serviceBId)
    {
        await using var context = CreateContext(tenantId);

        context.Tenants.Add(
            new Tenant(tenantId, "Slots HTTP Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Slots HTTP Business",
                CreatedAt,
                slotIntervalMinutes: 20));

        await context.SaveChangesAsync();

        context.Locations.Add(
            new Location(
                locationId, tenantId, businessId,
                "Location", "BR", "America/Sao_Paulo",
                CreatedAt));

        context.Professionals.Add(
            new Professional(
                professionalId, tenantId, businessId,
                "Professional", CreatedAt));

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

        context.ProfessionalLocations.Add(
            new ProfessionalLocation(
                tenantId, professionalId, locationId, CreatedAt));

        context.LocationServices.AddRange(
            new LocationService(
                tenantId, locationId, serviceAId, CreatedAt),
            new LocationService(
                tenantId, locationId, serviceBId, CreatedAt));

        context.ProfessionalServices.AddRange(
            new ProfessionalService(
                tenantId, professionalId, serviceAId, CreatedAt),
            new ProfessionalService(
                tenantId, professionalId, serviceBId, CreatedAt));

        await context.SaveChangesAsync();

        context.AvailabilityRules.Add(
            new AvailabilityRule(
                Guid.NewGuid(), tenantId, locationId,
                professionalId, DayOfWeek.Monday,
                new TimeOnly(9, 0), new TimeOnly(12, 0),
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        context.ScheduleBlocks.RemoveRange(
            await context.ScheduleBlocks.ToListAsync());
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

        context.ServiceComponents.RemoveRange(
            await context.ServiceComponents.ToListAsync());
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
        context.IdempotencyRecords.RemoveRange(
            await context.IdempotencyRecords.ToListAsync());
        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(x => x.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }
}
