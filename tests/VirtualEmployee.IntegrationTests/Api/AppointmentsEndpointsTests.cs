using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Customers;
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
public sealed class AppointmentsEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public AppointmentsEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task Appointments_ShouldRemoveOccupiedSlotsAndRestoreAfterExpiration()
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            var searchPayload = new
            {
                locationId = data.LocationId,
                serviceIds = new[] { data.ServiceId },
                date = "2026-10-19",
                professionalId = data.ProfessionalId
            };

            using var beforeResponse = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                searchPayload);

            Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);

            var before = await beforeResponse.Content.ReadFromJsonAsync<
                VirtualEmployee.Application.AvailabilitySlots
                    .SearchAvailableSlots.SearchAvailableSlotsResponse>();

            Assert.NotNull(before);
            Assert.NotEmpty(before.Slots);

            var startsAt = new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

            Assert.Contains(
                before.Slots,
                slot => slot.StartsAt == startsAt);

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                "appointment-slot-integration");

            using var createResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                new
                {
                    customerId = data.CustomerId,
                    locationId = data.LocationId,
                    professionalId = data.ProfessionalId,
                    serviceIds = new[] { data.ServiceId },
                    startsAt
                });

            Assert.Equal(
                HttpStatusCode.Created,
                createResponse.StatusCode);

            var created = await createResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(created);

            using var occupiedResponse = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                searchPayload);

            Assert.Equal(HttpStatusCode.OK, occupiedResponse.StatusCode);

            var occupied = await occupiedResponse.Content.ReadFromJsonAsync<
                VirtualEmployee.Application.AvailabilitySlots
                    .SearchAvailableSlots.SearchAvailableSlotsResponse>();

            Assert.NotNull(occupied);

            var expectedAvailable = before.Slots
                .Where(slot =>
                    !(slot.StartsAt < created.EndsAt &&
                      created.StartsAt < slot.EndsAt))
                .ToArray();

            Assert.True(expectedAvailable.Length < before.Slots.Count);

            Assert.Equal(
                expectedAvailable,
                occupied.Slots.ToArray());

            // O intervalo iniciado exatamente no fim permanece disponível.
            Assert.Contains(
                occupied.Slots,
                slot => slot.StartsAt == created.EndsAt);

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == created.Id);

                Assert.True(
                    appointment.Expire(CreatedAt.AddMinutes(10)));

                await context.SaveChangesAsync();
            }

            using var restoredResponse = await client.PostAsJsonAsync(
                "/api/v1/availability/slots/search",
                searchPayload);

            Assert.Equal(HttpStatusCode.OK, restoredResponse.StatusCode);

            var restored = await restoredResponse.Content.ReadFromJsonAsync<
                VirtualEmployee.Application.AvailabilitySlots
                    .SearchAvailableSlots.SearchAvailableSlotsResponse>();

            Assert.NotNull(restored);

            Assert.Equal(
                before.Slots.ToArray(),
                restored.Slots.ToArray());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointments_GetById_ShouldReturnCurrentExpiredState()
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                "appointment-current-state");

            var payload = new
            {
                customerId = data.CustomerId,
                locationId = data.LocationId,
                professionalId = data.ProfessionalId,
                serviceIds = new[] { data.ServiceId },
                startsAt = new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
            };

            using var createResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                payload);

            Assert.Equal(
                HttpStatusCode.Created,
                createResponse.StatusCode);

            var created = await createResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(created);
            Assert.Equal("PENDING", created.Status);

            var expiredAt = CreatedAt.AddMinutes(10);

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == created.Id);

                Assert.True(appointment.Expire(expiredAt));

                await context.SaveChangesAsync();
            }

            using var getResponse = await client.GetAsync(
                $"/api/v1/appointments/{created.Id}");

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var current = await getResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(current);
            Assert.Equal(created.Id, current.Id);
            Assert.Equal("EXPIRED", current.Status);
            Assert.Equal(expiredAt, current.UpdatedAt);
            Assert.Equal(created.CreatedAt, current.CreatedAt);
            Assert.Equal(created.TotalPrice, current.TotalPrice);
            Assert.Equal(
                created.Items.ToArray(),
                current.Items.ToArray());

            using var replayResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                payload);

            Assert.Equal(
                HttpStatusCode.Created,
                replayResponse.StatusCode);

            var replay = await replayResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(replay);
            Assert.Equal(created.Id, replay.Id);
            Assert.Equal("PENDING", replay.Status);
            Assert.Equal(created.UpdatedAt, replay.UpdatedAt);

            await using var verificationContext =
                CreateContext(data.TenantId);

            var persisted = await verificationContext.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(AppointmentStatus.Expired, persisted.Status);
            Assert.Equal(expiredAt, persisted.UpdatedAt);

            Assert.Equal(
                1,
                await verificationContext.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointments_GetById_ShouldRespectTenantAndLookupStatuses()
    {
        var dataA = new TestData();
        var dataB = new TestData();

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using var factory = CreateFactory();

            using var clientA = factory.CreateClient();
            clientA.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                dataA.TenantId.ToString());
            clientA.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                "appointment-tenant-lookup");

            using var createResponse = await clientA.PostAsJsonAsync(
                "/api/v1/appointments",
                new
                {
                    customerId = dataA.CustomerId,
                    locationId = dataA.LocationId,
                    professionalId = dataA.ProfessionalId,
                    serviceIds = new[] { dataA.ServiceId },
                    startsAt = new DateTimeOffset(
                        2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
                });

            Assert.Equal(
                HttpStatusCode.Created,
                createResponse.StatusCode);

            var created = await createResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(created);

            using var ownResponse = await clientA.GetAsync(
                $"/api/v1/appointments/{created.Id}");

            Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

            using var clientB = factory.CreateClient();
            clientB.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                dataB.TenantId.ToString());

            using var foreignResponse = await clientB.GetAsync(
                $"/api/v1/appointments/{created.Id}");

            Assert.Equal(
                HttpStatusCode.NotFound,
                foreignResponse.StatusCode);

            using var missingResponse = await clientB.GetAsync(
                $"/api/v1/appointments/{Guid.NewGuid()}");

            Assert.Equal(
                HttpStatusCode.NotFound,
                missingResponse.StatusCode);

            using var emptyIdResponse = await clientB.GetAsync(
                $"/api/v1/appointments/{Guid.Empty}");

            Assert.Equal(
                HttpStatusCode.BadRequest,
                emptyIdResponse.StatusCode);

            await using var context = CreateContext(dataA.TenantId);

            Assert.Equal(1, await context.Appointments.CountAsync());
            Assert.Equal(1, await context.AppointmentItems.CountAsync());
            Assert.Equal(1, await context.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Fact]
    public async Task Appointments_ShouldCreateReplayAndReturnCurrentReservation()
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                "appointment-http-create");

            var payload = new
            {
                customerId = data.CustomerId,
                locationId = data.LocationId,
                professionalId = data.ProfessionalId,
                serviceIds = new[] { data.ServiceId },
                startsAt = new DateTimeOffset(
                    2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3))
            };

            using var createResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                payload);

            Assert.Equal(
                HttpStatusCode.Created,
                createResponse.StatusCode);

            var created = await createResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(created);
            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal(data.BusinessId, created.BusinessId);
            Assert.Equal(data.CustomerId, created.CustomerId);
            Assert.Equal(data.LocationId, created.LocationId);
            Assert.Equal(data.ProfessionalId, created.ProfessionalId);
            Assert.Equal("PENDING", created.Status);
            Assert.Equal(65m, created.TotalPrice);
            Assert.Equal(45, created.TotalDurationMinutes);
            Assert.Equal(
                payload.startsAt.ToUniversalTime(),
                created.StartsAt);
            Assert.Equal(
                created.StartsAt.AddMinutes(45),
                created.EndsAt);
            Assert.Equal<DateTimeOffset?>(
                CreatedAt.AddMinutes(10),
                created.ReservationExpiresAt);

            var item = Assert.Single(created.Items);
            Assert.Equal(data.ServiceId, item.ServiceId);
            Assert.Equal("Corte", item.Name);
            Assert.Equal(65m, item.Price);
            Assert.Equal(45, item.DurationMinutes);

            var resourcePath = $"/api/v1/appointments/{created.Id}";

            Assert.NotNull(createResponse.Headers.Location);
            Assert.Equal(
                resourcePath,
                createResponse.Headers.Location.OriginalString);

            using var replayResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                payload);

            Assert.Equal(
                HttpStatusCode.Created,
                replayResponse.StatusCode);

            var replay = await replayResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(replay);
            Assert.Equal(created.Id, replay.Id);
            Assert.Equal(created.CreatedAt, replay.CreatedAt);
            Assert.Equal(created.UpdatedAt, replay.UpdatedAt);
            Assert.Equal(
                created.Items.ToArray(),
                replay.Items.ToArray());

            using var getResponse = await client.GetAsync(resourcePath);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var fetched = await getResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched.Id);
            Assert.Equal("PENDING", fetched.Status);
            Assert.Equal(created.StartsAt, fetched.StartsAt);
            Assert.Equal(created.EndsAt, fetched.EndsAt);
            Assert.Equal(created.TotalPrice, fetched.TotalPrice);
            Assert.Equal(
                created.Items.ToArray(),
                fetched.Items.ToArray());

            await using var context = CreateContext(data.TenantId);

            Assert.Equal(1, await context.Appointments.CountAsync());
            Assert.Equal(1, await context.AppointmentItems.CountAsync());
            Assert.Equal(1, await context.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("customer")]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("service")]
    public async Task Appointments_WithCrossTenantResource_ShouldReturnNotFound(
        string resource)
    {
        var dataA = new TestData();
        var dataB = new TestData();

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                dataA.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                $"appointment-cross-tenant-{resource}");

            using var response = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                new
                {
                    customerId = resource == "customer"
                        ? dataB.CustomerId
                        : dataA.CustomerId,
                    locationId = resource == "location"
                        ? dataB.LocationId
                        : dataA.LocationId,
                    professionalId = resource == "professional"
                        ? dataB.ProfessionalId
                        : dataA.ProfessionalId,
                    serviceIds = new[]
                    {
                        resource == "service"
                            ? dataB.ServiceId
                            : dataA.ServiceId
                    },
                    startsAt = new DateTimeOffset(
                        2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var context = CreateContext(dataA.TenantId);

            Assert.False(
                await context.Appointments
                    .IgnoreQueryFilters()
                    .AnyAsync(entity =>
                        entity.TenantId == dataA.TenantId ||
                        entity.TenantId == dataB.TenantId));

            Assert.False(
                await context.AppointmentItems
                    .IgnoreQueryFilters()
                    .AnyAsync(entity =>
                        entity.TenantId == dataA.TenantId ||
                        entity.TenantId == dataB.TenantId));

            Assert.False(
                await context.IdempotencyRecords
                    .IgnoreQueryFilters()
                    .AnyAsync(entity =>
                        entity.TenantId == dataA.TenantId ||
                        entity.TenantId == dataB.TenantId));
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("long-key")]
    [InlineData("missing-customer")]
    [InlineData("empty-location")]
    [InlineData("empty-professional")]
    [InlineData("missing-services")]
    [InlineData("null-services")]
    [InlineData("empty-services")]
    [InlineData("duplicate-services")]
    [InlineData("empty-service-id")]
    [InlineData("missing-start")]
    [InlineData("malformed-start")]
    [InlineData("submicrosecond-start")]
    [InlineData("malformed-customer")]
    public async Task Appointments_WithInvalidRequest_ShouldReturnBadRequest(
        string scenario)
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            if (scenario != "missing-key")
            {
                client.DefaultRequestHeaders.Add(
                    "Idempotency-Key",
                    scenario == "long-key"
                        ? new string('a', 201)
                        : "appointment-http-invalid");
            }

            var payload = new Dictionary<string, object?>
            {
                ["customerId"] = data.CustomerId,
                ["locationId"] = data.LocationId,
                ["professionalId"] = data.ProfessionalId,
                ["serviceIds"] = new[] { data.ServiceId },
                ["startsAt"] = new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
            };

            switch (scenario)
            {
                case "missing-customer":
                    payload.Remove("customerId");
                    break;

                case "empty-location":
                    payload["locationId"] = Guid.Empty;
                    break;

                case "empty-professional":
                    payload["professionalId"] = Guid.Empty;
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

                case "duplicate-services":
                    payload["serviceIds"] =
                        new[] { data.ServiceId, data.ServiceId };
                    break;

                case "empty-service-id":
                    payload["serviceIds"] = new[] { Guid.Empty };
                    break;

                case "missing-start":
                    payload.Remove("startsAt");
                    break;

                case "malformed-start":
                    payload["startsAt"] = "invalid-date";
                    break;

                case "submicrosecond-start":
                    payload["startsAt"] =
                        new DateTimeOffset(
                            2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
                        .AddTicks(1);
                    break;

                case "malformed-customer":
                    payload["customerId"] = "invalid-guid";
                    break;
            }

            using var response = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                payload);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            await using var context = CreateContext(data.TenantId);

            Assert.False(await context.Appointments.AnyAsync());
            Assert.False(await context.AppointmentItems.AnyAsync());
            Assert.False(await context.IdempotencyRecords.AnyAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("occupied", "SLOT_UNAVAILABLE")]
    [InlineData("different-content", "IDEMPOTENCY_CONFLICT")]
    public async Task Appointments_WithConflict_ShouldReturnCodeWithoutPersisting(
        string scenario,
        string expectedCode)
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                "appointment-http-first");

            var startsAt = new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

            var payload = new
            {
                customerId = data.CustomerId,
                locationId = data.LocationId,
                professionalId = data.ProfessionalId,
                serviceIds = new[] { data.ServiceId },
                startsAt
            };

            using var firstResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                payload);

            Assert.Equal(
                HttpStatusCode.Created,
                firstResponse.StatusCode);

            var created = await firstResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(created);

            if (scenario == "occupied")
            {
                client.DefaultRequestHeaders.Remove("Idempotency-Key");
                client.DefaultRequestHeaders.Add(
                    "Idempotency-Key",
                    "appointment-http-second");
            }

            var conflictingPayload = new
            {
                customerId = data.CustomerId,
                locationId = data.LocationId,
                professionalId = data.ProfessionalId,
                serviceIds = new[] { data.ServiceId },
                startsAt = scenario == "different-content"
                    ? startsAt.AddMinutes(15)
                    : startsAt
            };

            using var conflictResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                conflictingPayload);

            Assert.Equal(
                HttpStatusCode.Conflict,
                conflictResponse.StatusCode);

            var json = await conflictResponse.Content.ReadAsStringAsync();

            using var problem = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal(
                expectedCode,
                problem.RootElement.GetProperty("code").GetString());

            Assert.Equal(
                409,
                problem.RootElement.GetProperty("status").GetInt32());

            await using var context = CreateContext(data.TenantId);

            var persisted = await context.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(created.Id, persisted.Id);
            Assert.Equal(startsAt, persisted.StartsAt);

            Assert.Equal(1, await context.AppointmentItems.CountAsync());
            Assert.Equal(1, await context.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    #endregion

    #region Helpers

    private sealed class TestData
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid ProfessionalId { get; } = Guid.NewGuid();
        public Guid ServiceId { get; } = Guid.NewGuid();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => CreatedAt;
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

                builder.UseSetting(
                    "Scheduling:Reservations:LifetimeMinutes",
                    "10");

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<AppDbContext>();
                    services.RemoveAll<TimeProvider>();

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_fixture.ConnectionString));

                    services.AddSingleton<TimeProvider>(
                        new FixedTimeProvider());
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

    private async Task SeedAsync(TestData data)
    {
        await using var context = CreateContext(data.TenantId);

        context.Tenants.Add(
            new Tenant(data.TenantId, "Appointment HTTP Tenant"));

        context.Businesses.Add(
            new Business(
                data.BusinessId,
                data.TenantId,
                BusinessTypeIds.Barbershop,
                "Appointment HTTP Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Locations.Add(
            new Location(
                data.LocationId,
                data.TenantId,
                data.BusinessId,
                "Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));

        context.Customers.Add(
            new Customer(
                data.CustomerId,
                data.TenantId,
                data.BusinessId,
                "+5511999990001",
                CreatedAt,
                "Customer"));

        context.Professionals.Add(
            new Professional(
                data.ProfessionalId,
                data.TenantId,
                data.BusinessId,
                "Professional",
                CreatedAt));

        context.Services.Add(
            new Service(
                data.ServiceId,
                data.TenantId,
                data.BusinessId,
                "Corte",
                ServiceType.Single,
                65m,
                45,
                CreatedAt));

        await context.SaveChangesAsync();

        context.ProfessionalLocations.Add(
            new ProfessionalLocation(
                data.TenantId,
                data.ProfessionalId,
                data.LocationId,
                CreatedAt));

        context.ProfessionalServices.Add(
            new ProfessionalService(
                data.TenantId,
                data.ProfessionalId,
                data.ServiceId,
                CreatedAt));

        context.LocationServices.Add(
            new LocationService(
                data.TenantId,
                data.LocationId,
                data.ServiceId,
                CreatedAt));

        await context.SaveChangesAsync();

        context.AvailabilityRules.Add(
            new AvailabilityRule(
                Guid.NewGuid(),
                data.TenantId,
                data.LocationId,
                data.ProfessionalId,
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(18, 0),
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        context.AppointmentItems.RemoveRange(
            await context.AppointmentItems.ToListAsync());
        await context.SaveChangesAsync();

        context.Appointments.RemoveRange(
            await context.Appointments.ToListAsync());
        await context.SaveChangesAsync();

        context.ScheduleBlocks.RemoveRange(
            await context.ScheduleBlocks.ToListAsync());
        context.AvailabilityRules.RemoveRange(
            await context.AvailabilityRules.ToListAsync());
        context.ProfessionalServices.RemoveRange(
            await context.ProfessionalServices.ToListAsync());
        context.LocationServices.RemoveRange(
            await context.LocationServices.ToListAsync());

        await context.SaveChangesAsync();

        context.ProfessionalLocations.RemoveRange(
            await context.ProfessionalLocations.ToListAsync());
        await context.SaveChangesAsync();

        context.Customers.RemoveRange(
            await context.Customers.ToListAsync());
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
            .SingleOrDefaultAsync(entity => entity.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }

    #endregion
}