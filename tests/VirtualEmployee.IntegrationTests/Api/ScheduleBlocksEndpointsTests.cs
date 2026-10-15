using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.ScheduleBlocks;
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
public sealed class ScheduleBlocksEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ScheduleBlocksEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

     [Fact]
    public async Task ScheduleBlocks_WithoutIdempotencyKey_ShouldRejectWithoutPersisting()
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

            using var response = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                new
                {
                    locationId,
                    professionalId,
                    startsAt = "2026-10-19T09:00:00-03:00",
                    endsAt = "2026-10-19T10:00:00-03:00"
                });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            using var problem = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            Assert.Equal(
                "Invalid schedule block request",
                problem.RootElement.GetProperty("title").GetString());

            await using var context = CreateContext(tenantId);

            Assert.False(await context.ScheduleBlocks.AnyAsync());
            Assert.False(await context.IdempotencyRecords.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ScheduleBlocks_FromAnotherTenant_ShouldReturnNotFoundWithoutPersisting()
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                otherTenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString("N"));

            using var createResponse = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                new
                {
                    locationId,
                    professionalId,
                    startsAt = "2026-10-19T09:00:00-03:00",
                    endsAt = "2026-10-19T10:00:00-03:00"
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                createResponse.StatusCode);

            using var getResponse = await client.GetAsync(
                $"/api/v1/schedule-blocks?locationId={locationId}" +
                "&from=2026-10-19T00:00:00Z" +
                "&to=2026-10-20T00:00:00Z");

            Assert.Equal(
                HttpStatusCode.NotFound,
                getResponse.StatusCode);

            await using (var otherContext = CreateContext(otherTenantId))
            {
                Assert.False(await otherContext.ScheduleBlocks.AnyAsync());
                Assert.False(await otherContext.IdempotencyRecords.AnyAsync());
            }

            await using var ownerContext = CreateContext(ownerTenantId);

            Assert.False(await ownerContext.ScheduleBlocks.AnyAsync());
            Assert.False(await ownerContext.IdempotencyRecords.AnyAsync());
            Assert.True(
                await ownerContext.Locations.AnyAsync(x => x.Id == locationId));
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Fact]
    public async Task ScheduleBlocks_Delete_ShouldDeactivateAndPreserveOriginalReplay()
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

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString("N"));

            var payload = new
            {
                locationId,
                professionalId,
                startsAt = "2026-10-19T12:00:00Z",
                endsAt = "2026-10-19T13:00:00Z"
            };

            using var createResponse = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                payload);

            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var created = await createResponse.Content
                .ReadFromJsonAsync<ScheduleBlockResponse>();

            Assert.NotNull(created);

            using var deleteResponse = await client.DeleteAsync(
                $"/api/v1/schedule-blocks/{created.Id}");

            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
            Assert.Equal(
                string.Empty,
                await deleteResponse.Content.ReadAsStringAsync());

            DateTimeOffset deactivatedAt;

            await using (var context = CreateContext(tenantId))
            {
                var block = await context.ScheduleBlocks
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == created.Id);

                Assert.False(block.IsActive);
                Assert.Equal(created.CreatedAt, block.CreatedAt);
                Assert.Equal(created.StartsAt, block.StartsAt);
                Assert.Equal(created.EndsAt, block.EndsAt);

                deactivatedAt = block.UpdatedAt;
            }

            // Usa outro horário para detectar alteração indevida na repetição.
            var laterTime = CreatedAt.AddHours(1);

            await using var laterFactory = CreateFactory(laterTime);
            using var laterClient = laterFactory.CreateClient();

            laterClient.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var repeatedDelete = await laterClient.DeleteAsync(
                $"/api/v1/schedule-blocks/{created.Id}");

            Assert.Equal(HttpStatusCode.NoContent, repeatedDelete.StatusCode);

            using var getResponse = await client.GetAsync(
                $"/api/v1/schedule-blocks?locationId={locationId}" +
                "&from=2026-10-19T11:00:00Z" +
                "&to=2026-10-19T14:00:00Z");

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var blocks = await getResponse.Content
                .ReadFromJsonAsync<List<ScheduleBlockResponse>>();

            Assert.NotNull(blocks);
            Assert.Empty(blocks);

            using var replayResponse = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                payload);

            Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);

            var replay = await replayResponse.Content
                .ReadFromJsonAsync<ScheduleBlockResponse>();

            Assert.Equal(created, replay);

            await using var verificationContext = CreateContext(tenantId);

            var persistedBlock = await verificationContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(created.Id, persistedBlock.Id);
            Assert.False(persistedBlock.IsActive);
            Assert.Equal(deactivatedAt, persistedBlock.UpdatedAt);
            Assert.Equal(
                1,
                await verificationContext.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleBlocks_ShouldCreateReplayRejectConflictAndReturnStoredBlock(
        bool professionalScoped)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");

        Guid? selectedProfessionalId =
            professionalScoped ? professionalId : null;

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

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                key);

            var payload = new
            {
                locationId,
                professionalId = selectedProfessionalId,
                startsAt = "2026-10-19T09:00:00-03:00",
                endsAt = "2026-10-19T10:00:00-03:00"
            };

            using var createResponse = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                payload);

            Assert.Equal(
                HttpStatusCode.Created,
                createResponse.StatusCode);

            var created = await createResponse.Content
                .ReadFromJsonAsync<ScheduleBlockResponse>();

            Assert.NotNull(created);
            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal(locationId, created.LocationId);
            Assert.Equal(selectedProfessionalId, created.ProfessionalId);
            Assert.True(created.IsActive);
            Assert.Equal(CreatedAt, created.CreatedAt);
            Assert.Equal(CreatedAt, created.UpdatedAt);

            Assert.Equal(
                new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
                created.StartsAt);

            Assert.Equal(created.StartsAt.AddHours(1), created.EndsAt);

            // Mesmos instantes em UTC: deve reproduzir a resposta original.
            using var replayResponse = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                new
                {
                    locationId,
                    professionalId = selectedProfessionalId,
                    startsAt = "2026-10-19T12:00:00+00:00",
                    endsAt = "2026-10-19T13:00:00+00:00"
                });

            Assert.Equal(
                HttpStatusCode.Created,
                replayResponse.StatusCode);

            var replay = await replayResponse.Content
                .ReadFromJsonAsync<ScheduleBlockResponse>();

            Assert.Equal(created, replay);

            using var conflictResponse = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                new
                {
                    locationId,
                    professionalId = selectedProfessionalId,
                    startsAt = "2026-10-19T12:00:00+00:00",
                    endsAt = "2026-10-19T14:00:00+00:00"
                });

            Assert.Equal(
                HttpStatusCode.Conflict,
                conflictResponse.StatusCode);

            using var problem = JsonDocument.Parse(
                await conflictResponse.Content.ReadAsStringAsync());

            Assert.Equal(
                "IDEMPOTENCY_CONFLICT",
                problem.RootElement.GetProperty("code").GetString());

            var query =
                $"/api/v1/schedule-blocks?locationId={locationId}" +
                "&from=2026-10-19T11:00:00Z" +
                "&to=2026-10-19T14:00:00Z" +
                $"&professionalId={professionalId}";

            using var getResponse = await client.GetAsync(query);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var blocks = await getResponse.Content
                .ReadFromJsonAsync<List<ScheduleBlockResponse>>();

            Assert.NotNull(blocks);
            Assert.Equal(created, Assert.Single(blocks));

            await using var context = CreateContext(tenantId);

            var persistedBlock = await context.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(created.Id, persistedBlock.Id);
            Assert.Equal(created.StartsAt, persistedBlock.StartsAt);
            Assert.Equal(created.EndsAt, persistedBlock.EndsAt);

            var record = await context.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(key, record.Key);
            Assert.Equal("CreateScheduleBlock", record.Operation);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(
        "?locationId=00000000-0000-0000-0000-000000000000&from=2026-10-19T12:00:00Z&to=2026-10-19T13:00:00Z")]
    [InlineData(
        "?locationId=11111111-1111-4111-8111-111111111111&to=2026-10-19T13:00:00Z")]
    [InlineData(
        "?locationId=11111111-1111-4111-8111-111111111111&from=2026-10-19T12:00:00Z")]
    [InlineData(
        "?locationId=11111111-1111-4111-8111-111111111111&from=2026-10-19T12:00:00Z&to=2026-10-19T12:00:00Z")]
    [InlineData(
        "?locationId=11111111-1111-4111-8111-111111111111&from=2026-10-19T13:00:00Z&to=2026-10-19T12:00:00Z")]
    [InlineData(
        "?locationId=11111111-1111-4111-8111-111111111111&from=2026-10-19T12:00:00Z&to=2026-10-19T13:00:00Z&professionalId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?locationId=invalid-guid")]
    [InlineData(
        "?locationId=11111111-1111-4111-8111-111111111111&from=invalid-date&to=2026-10-19T13:00:00Z")]
    public async Task GetScheduleBlocks_WithInvalidQuery_ShouldReturnBadRequest(
        string query)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add(
            "X-Tenant-Id",
            Guid.NewGuid().ToString());

        using var response = await client.GetAsync(
            "/api/v1/schedule-blocks" + query);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("missing-location")]
    [InlineData("empty-location")]
    [InlineData("empty-professional")]
    [InlineData("missing-start")]
    [InlineData("missing-end")]
    [InlineData("equal-times")]
    [InlineData("reversed-times")]
    public async Task CreateScheduleBlock_WithInvalidPayload_ShouldNotPersist(
        string scenario)
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

            var payload = new Dictionary<string, object?>
            {
                ["locationId"] = locationId,
                ["professionalId"] = professionalId,
                ["startsAt"] = "2026-10-19T12:00:00Z",
                ["endsAt"] = "2026-10-19T13:00:00Z"
            };

            switch (scenario)
            {
                case "missing-location":
                    payload.Remove("locationId");
                    break;

                case "empty-location":
                    payload["locationId"] = Guid.Empty;
                    break;

                case "empty-professional":
                    payload["professionalId"] = Guid.Empty;
                    break;

                case "missing-start":
                    payload.Remove("startsAt");
                    break;

                case "missing-end":
                    payload.Remove("endsAt");
                    break;

                case "equal-times":
                    payload["endsAt"] = payload["startsAt"];
                    break;

                case "reversed-times":
                    payload["endsAt"] = "2026-10-19T11:00:00Z";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString("N"));

            using var response = await client.PostAsJsonAsync(
                "/api/v1/schedule-blocks",
                payload);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            using var problem = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            Assert.Equal(
                "Invalid schedule block request",
                problem.RootElement.GetProperty("title").GetString());

            await using var context = CreateContext(tenantId);

            Assert.False(await context.ScheduleBlocks.AnyAsync());
            Assert.False(await context.IdempotencyRecords.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("other-tenant")]
    [InlineData("missing-id")]
    [InlineData("empty-id")]
    public async Task ScheduleBlocks_DeleteWithInvalidTarget_ShouldPreserveOwnerBlock(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.ScheduleBlocks.Add(
                    new VirtualEmployee.Domain.Scheduling.ScheduleBlock(
                        blockId,
                        tenantId,
                        locationId,
                        professionalId,
                        CreatedAt.AddDays(1),
                        CreatedAt.AddDays(1).AddHours(1),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            var requestedId = scenario switch
            {
                "missing-id" => Guid.NewGuid(),
                "empty-id" => Guid.Empty,
                _ => blockId
            };

            var requestingTenantId = scenario == "other-tenant"
                ? Guid.NewGuid()
                : tenantId;

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                requestingTenantId.ToString());

            using var response = await client.DeleteAsync(
                $"/api/v1/schedule-blocks/{requestedId}");

            Assert.Equal(
                scenario == "empty-id"
                    ? HttpStatusCode.BadRequest
                    : HttpStatusCode.NotFound,
                response.StatusCode);

            await using var readContext = CreateContext(tenantId);

            var block = await readContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync(x => x.Id == blockId);

            Assert.True(block.IsActive);
            Assert.Equal(CreatedAt, block.CreatedAt);
            Assert.Equal(CreatedAt, block.UpdatedAt);
            Assert.False(await readContext.IdempotencyRecords.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }
    private WebApplicationFactory<Program> CreateFactory(
        DateTimeOffset? now = null)
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
                        new FixedTimeProvider(now ?? CreatedAt));
                });
            });
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
        Guid professionalId)
    {
        await using var context = CreateContext(tenantId);

        context.Tenants.Add(
            new Tenant(tenantId, "Blocks HTTP Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Blocks HTTP Business",
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

        context.ScheduleBlocks.RemoveRange(
            await context.ScheduleBlocks.ToListAsync());
        context.IdempotencyRecords.RemoveRange(
            await context.IdempotencyRecords.ToListAsync());
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