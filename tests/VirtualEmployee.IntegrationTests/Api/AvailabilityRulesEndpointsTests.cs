using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.AvailabilityRules;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class AvailabilityRulesEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public AvailabilityRulesEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AvailabilityRules_WithValidWindows_ShouldReplaceAndReturnOrderedList()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var uri =
                $"/api/v1/locations/{locationId}" +
                $"/professionals/{professionalId}/availability-rules";

            using var putResponse = await client.PutAsJsonAsync(
                uri,
                new
                {
                    rules = new[]
                    {
                        new
                        {
                            dayOfWeek = 2,
                            startTime = "13:00",
                            endTime = "18:00"
                        },
                        new
                        {
                            dayOfWeek = 1,
                            startTime = "12:00",
                            endTime = "18:00"
                        },
                        new
                        {
                            dayOfWeek = 1,
                            startTime = "09:00",
                            endTime = "12:00"
                        }
                    }
                });

            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
            Assert.Equal(
                string.Empty,
                await putResponse.Content.ReadAsStringAsync());

            using var getResponse = await client.GetAsync(uri);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var json = await getResponse.Content.ReadAsStringAsync();

            using (var document = JsonDocument.Parse(json))
            {
                var first = document.RootElement[0];

                Assert.Equal(
                    JsonValueKind.Number,
                    first.GetProperty("dayOfWeek").ValueKind);

                Assert.Equal(
                    "09:00:00",
                    first.GetProperty("startTime").GetString());

                Assert.Equal(
                    "12:00:00",
                    first.GetProperty("endTime").GetString());
            }

            var rules = JsonSerializer.Deserialize<
                List<AvailabilityRuleResponse>>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(rules);
            Assert.Equal(3, rules.Count);

            Assert.Equal(
                new[] { 1, 1, 2 },
                rules.Select(rule => rule.DayOfWeek).ToArray());

            Assert.Equal(
                new[]
                {
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0),
                    new TimeOnly(13, 0)
                },
                rules.Select(rule => rule.StartTime).ToArray());

            Assert.Equal(
                new[]
                {
                    new TimeOnly(12, 0),
                    new TimeOnly(18, 0),
                    new TimeOnly(18, 0)
                },
                rules.Select(rule => rule.EndTime).ToArray());

            Assert.All(rules, rule => Assert.NotEqual(Guid.Empty, rule.Id));
            Assert.Equal(3, rules.Select(rule => rule.Id).Distinct().Count());
        }
        finally
        {
            await CleanupAsync(tenantId);
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

    [Fact]
    public async Task AvailabilityRules_WithSundayAndMidnight_ShouldAcceptZeroValues()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var uri =
                $"/api/v1/locations/{locationId}/professionals/" +
                $"{professionalId}/availability-rules";

            using var putResponse = await client.PutAsJsonAsync(
                uri,
                new
                {
                    rules = new[]
                    {
                        new
                        {
                            dayOfWeek = 0,
                            startTime = "00:00",
                            endTime = "02:00"
                        }
                    }
                });

            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

            using var getResponse = await client.GetAsync(uri);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var rules = await getResponse.Content
                .ReadFromJsonAsync<List<AvailabilityRuleResponse>>();

            Assert.NotNull(rules);

            var rule = Assert.Single(rules);

            Assert.NotEqual(Guid.Empty, rule.Id);
            Assert.Equal(0, rule.DayOfWeek);
            Assert.Equal(new TimeOnly(0, 0), rule.StartTime);
            Assert.Equal(new TimeOnly(2, 0), rule.EndTime);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task AvailabilityRules_WithEmptyList_ShouldDeactivateExistingRules()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.AvailabilityRules.Add(
                    new VirtualEmployee.Domain.Scheduling.AvailabilityRule(
                        ruleId,
                        tenantId,
                        locationId,
                        professionalId,
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

            var uri =
                $"/api/v1/locations/{locationId}/professionals/" +
                $"{professionalId}/availability-rules";

            using var putResponse = await client.PutAsJsonAsync(
                uri,
                new { rules = Array.Empty<object>() });

            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
            Assert.Equal(
                string.Empty,
                await putResponse.Content.ReadAsStringAsync());

            using var getResponse = await client.GetAsync(uri);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var rules = await getResponse.Content
                .ReadFromJsonAsync<List<AvailabilityRuleResponse>>();

            Assert.NotNull(rules);
            Assert.Empty(rules);

            await using var readContext = CreateContext(tenantId);

            var persistedRule = await readContext.AvailabilityRules
                .AsNoTracking()
                .SingleAsync(x =>
                    x.LocationId == locationId &&
                    x.ProfessionalId == professionalId);

            Assert.Equal(ruleId, persistedRule.Id);
            Assert.False(persistedRule.IsActive);
            Assert.Equal(CreatedAt, persistedRule.CreatedAt);
            Assert.True(persistedRule.UpdatedAt > CreatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task AvailabilityRules_FromAnotherTenant_ShouldReturnNotFoundAndPreserveAgenda()
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(ownerTenantId))
            {
                context.AvailabilityRules.Add(
                    new VirtualEmployee.Domain.Scheduling.AvailabilityRule(
                        ruleId,
                        ownerTenantId,
                        locationId,
                        professionalId,
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
                otherTenantId.ToString());

            var uri =
                $"/api/v1/locations/{locationId}/professionals/" +
                $"{professionalId}/availability-rules";

            using var getResponse = await client.GetAsync(uri);

            Assert.Equal(
                HttpStatusCode.NotFound,
                getResponse.StatusCode);

            using var putResponse = await client.PutAsJsonAsync(
                uri,
                new { rules = Array.Empty<object>() });

            Assert.Equal(
                HttpStatusCode.NotFound,
                putResponse.StatusCode);

            await using var readContext = CreateContext(ownerTenantId);

            var persistedRule = await readContext.AvailabilityRules
                .AsNoTracking()
                .SingleAsync(x =>
                    x.LocationId == locationId &&
                    x.ProfessionalId == professionalId);

            Assert.Equal(ruleId, persistedRule.Id);
            Assert.Equal(ownerTenantId, persistedRule.TenantId);
            Assert.True(persistedRule.IsActive);
            Assert.Equal(DayOfWeek.Monday, persistedRule.DayOfWeek);
            Assert.Equal(new TimeOnly(9, 0), persistedRule.StartTime);
            Assert.Equal(new TimeOnly(12, 0), persistedRule.EndTime);
            Assert.Equal(CreatedAt, persistedRule.CreatedAt);
            Assert.Equal(CreatedAt, persistedRule.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Fact]
    public async Task AvailabilityRules_WithProfessionalFromAnotherBusiness_ShouldReturnConflict()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();
        var otherProfessionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.Businesses.Add(
                    new Business(
                        otherBusinessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Other Business",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.Professionals.Add(
                    new Professional(
                        otherProfessionalId,
                        tenantId,
                        otherBusinessId,
                        "Other Professional",
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var uri =
                $"/api/v1/locations/{locationId}/professionals/" +
                $"{otherProfessionalId}/availability-rules";

            using var putResponse = await client.PutAsJsonAsync(
                uri,
                new
                {
                    rules = new[]
                    {
                        new
                        {
                            dayOfWeek = 1,
                            startTime = "09:00",
                            endTime = "12:00"
                        }
                    }
                });

            Assert.Equal(
                HttpStatusCode.Conflict,
                putResponse.StatusCode);

            using var problem = JsonDocument.Parse(
                await putResponse.Content.ReadAsStringAsync());

            Assert.Equal(
                "Availability rules conflict",
                problem.RootElement.GetProperty("title").GetString());

            Assert.Equal(
                "Professional and Location must belong to the same Business.",
                problem.RootElement.GetProperty("detail").GetString());

            using var getResponse = await client.GetAsync(uri);

            Assert.Equal(
                HttpStatusCode.NotFound,
                getResponse.StatusCode);

            await using var readContext = CreateContext(tenantId);

            Assert.False(
                await readContext.AvailabilityRules.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"rules\":null}")]
    [InlineData("{\"rules\":[null]}")]
    [InlineData("{\"rules\":[{\"startTime\":\"09:00\",\"endTime\":\"12:00\"}]}")]
    [InlineData("{\"rules\":[{\"dayOfWeek\":1,\"endTime\":\"12:00\"}]}")]
    [InlineData("{\"rules\":[{\"dayOfWeek\":1,\"startTime\":\"09:00\"}]}")]
    [InlineData("{\"rules\":[{\"dayOfWeek\":7,\"startTime\":\"09:00\",\"endTime\":\"12:00\"}]}")]
    [InlineData("{\"rules\":[{\"dayOfWeek\":1,\"startTime\":\"12:00\",\"endTime\":\"09:00\"}]}")]
    [InlineData("{\"rules\":[{\"dayOfWeek\":1,\"startTime\":\"09:00\",\"endTime\":\"09:00\"}]}")]
    [InlineData("{\"rules\":[{\"dayOfWeek\":1,\"startTime\":\"09:00\",\"endTime\":\"12:00\"},{\"dayOfWeek\":1,\"startTime\":\"11:00\",\"endTime\":\"14:00\"}]}")]
    public async Task AvailabilityRules_WithInvalidPayload_ShouldPreserveAgenda(
        string payload)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.AvailabilityRules.Add(
                    new VirtualEmployee.Domain.Scheduling.AvailabilityRule(
                        ruleId,
                        tenantId,
                        locationId,
                        professionalId,
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

            var uri =
                $"/api/v1/locations/{locationId}" +
                $"/professionals/{professionalId}/availability-rules";

            using var content = new System.Net.Http.StringContent(
                payload,
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await client.PutAsync(uri, content);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            using var problem = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            Assert.Equal(
                "Invalid availability rules request",
                problem.RootElement.GetProperty("title").GetString());

            await using var readContext = CreateContext(tenantId);

            var persistedRules = await readContext.AvailabilityRules
                .AsNoTracking()
                .ToListAsync();

            var rule = Assert.Single(persistedRules);

            Assert.Equal(ruleId, rule.Id);
            Assert.Equal(DayOfWeek.Monday, rule.DayOfWeek);
            Assert.Equal(new TimeOnly(9, 0), rule.StartTime);
            Assert.Equal(new TimeOnly(12, 0), rule.EndTime);
            Assert.True(rule.IsActive);
            Assert.Equal(CreatedAt, rule.CreatedAt);
            Assert.Equal(CreatedAt, rule.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("link")]
    public async Task AvailabilityRules_WithMissingResource_ShouldReturnNotFound(
        string missingResource)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            if (missingResource == "link")
            {
                await using var context = CreateContext(tenantId);

                var link = await context.ProfessionalLocations
                    .SingleAsync(x =>
                        x.LocationId == locationId &&
                        x.ProfessionalId == professionalId);

                context.ProfessionalLocations.Remove(link);

                await context.SaveChangesAsync();
            }

            var requestedLocationId = missingResource == "location"
                ? Guid.NewGuid()
                : locationId;

            var requestedProfessionalId = missingResource == "professional"
                ? Guid.NewGuid()
                : professionalId;

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var uri =
                $"/api/v1/locations/{requestedLocationId}/professionals/" +
                $"{requestedProfessionalId}/availability-rules";

            using var getResponse = await client.GetAsync(uri);

            Assert.Equal(
                HttpStatusCode.NotFound,
                getResponse.StatusCode);

            using var putResponse = await client.PutAsJsonAsync(
                uri,
                new
                {
                    rules = new[]
                    {
                        new
                        {
                            dayOfWeek = 1,
                            startTime = "09:00",
                            endTime = "12:00"
                        }
                    }
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                putResponse.StatusCode);

            await using var readContext = CreateContext(tenantId);

            Assert.False(
                await readContext.AvailabilityRules.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("link")]
    public async Task AvailabilityRules_WithInactiveResource_ShouldPreserveAgenda(
        string inactiveResource)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.AvailabilityRules.Add(
                    new VirtualEmployee.Domain.Scheduling.AvailabilityRule(
                        ruleId,
                        tenantId,
                        locationId,
                        professionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(12, 0),
                        CreatedAt));

                if (inactiveResource == "location")
                {
                    var location = await context.Locations
                        .SingleAsync(x => x.Id == locationId);

                    location.Update(
                        location.Name,
                        location.CountryCode,
                        location.Timezone,
                        false,
                        CreatedAt.AddHours(1));
                }
                else if (inactiveResource == "professional")
                {
                    var professional = await context.Professionals
                        .SingleAsync(x => x.Id == professionalId);

                    professional.Update(
                        professional.Name,
                        false,
                        CreatedAt.AddHours(1));
                }
                else
                {
                    var link = await context.ProfessionalLocations
                        .SingleAsync(x =>
                            x.LocationId == locationId &&
                            x.ProfessionalId == professionalId);

                    link.Update(false, CreatedAt.AddHours(1));
                }

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var uri =
                $"/api/v1/locations/{locationId}/professionals/" +
                $"{professionalId}/availability-rules";

            using var putResponse = await client.PutAsJsonAsync(
                uri,
                new { rules = Array.Empty<object>() });

            Assert.Equal(
                HttpStatusCode.Conflict,
                putResponse.StatusCode);

            var json = await putResponse.Content.ReadAsStringAsync();

            using var problem = JsonDocument.Parse(json);

            Assert.Equal(
                "Availability rules conflict",
                problem.RootElement.GetProperty("title").GetString());

            // A consulta administrativa continua disponível.
            using var getResponse = await client.GetAsync(uri);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var rules = await getResponse.Content
                .ReadFromJsonAsync<List<AvailabilityRuleResponse>>();

            Assert.NotNull(rules);
            Assert.Equal(ruleId, Assert.Single(rules).Id);

            await using var readContext = CreateContext(tenantId);

            var persistedRule = await readContext.AvailabilityRules
                .AsNoTracking()
                .SingleAsync(x =>
                    x.LocationId == locationId &&
                    x.ProfessionalId == professionalId);

            Assert.Equal(ruleId, persistedRule.Id);
            Assert.True(persistedRule.IsActive);
            Assert.Equal(DayOfWeek.Monday, persistedRule.DayOfWeek);
            Assert.Equal(new TimeOnly(9, 0), persistedRule.StartTime);
            Assert.Equal(new TimeOnly(12, 0), persistedRule.EndTime);
            Assert.Equal(CreatedAt, persistedRule.CreatedAt);
            Assert.Equal(CreatedAt, persistedRule.UpdatedAt);
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

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_fixture.ConnectionString));
                });
            });
    }

    private async Task SeedAsync(
        Guid tenantId,
        Guid businessId,
        Guid locationId,
        Guid professionalId)
    {
        await using var context = CreateContext(tenantId);

        context.Tenants.Add(
            new Tenant(tenantId, "Availability HTTP Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Availability HTTP Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Locations.Add(
            new Location(
                locationId,
                tenantId,
                businessId,
                "Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));

        context.Professionals.Add(
            new Professional(
                professionalId,
                tenantId,
                businessId,
                "Professional",
                CreatedAt));

        await context.SaveChangesAsync();

        context.ProfessionalLocations.Add(
            new ProfessionalLocation(
                tenantId,
                professionalId,
                locationId,
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        context.AvailabilityRules.RemoveRange(
            await context.AvailabilityRules.ToListAsync());
        await context.SaveChangesAsync();

        context.ProfessionalLocations.RemoveRange(
            await context.ProfessionalLocations.ToListAsync());
        await context.SaveChangesAsync();

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
}