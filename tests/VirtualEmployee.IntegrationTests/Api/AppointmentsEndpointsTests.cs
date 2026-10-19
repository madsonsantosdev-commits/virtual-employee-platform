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
using VirtualEmployee.Application.Appointments.GetAppointments;
using VirtualEmployee.Application.Appointments.CancelAppointment;
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
    public async Task Appointments_Cancel_ShouldPersistAndReplay()
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString());

            var startsAt = new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

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

            var createBody = await createResponse.Content
                .ReadAsStringAsync();

            Assert.True(
                createResponse.StatusCode == HttpStatusCode.Created,
                $"Criação HTTP: {(int)createResponse.StatusCode}" +
                Environment.NewLine +
                createBody);

            var created = await createResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(created);

            var cancellationKey = Guid.NewGuid().ToString();

            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                cancellationKey);

            var url = $"/api/v1/appointments/{created.Id}/cancel";

            using var cancelResponse = await client.PostAsJsonAsync(
                url,
                new
                {
                    cancelledBy = "CLIENT",
                    reason = "  Cliente solicitou  "
                });

            var cancelBody = await cancelResponse.Content
                .ReadAsStringAsync();

            Assert.True(
                cancelResponse.StatusCode == HttpStatusCode.OK,
                $"Cancelamento HTTP: {(int)cancelResponse.StatusCode}" +
                Environment.NewLine +
                cancelBody);

            var cancelled = await cancelResponse.Content
                .ReadFromJsonAsync<CancelAppointmentResponse>();

            Assert.NotNull(cancelled);
            Assert.Equal(created.Id, cancelled.Id);
            Assert.Equal("CANCELLED_BY_CLIENT", cancelled.Status);
            Assert.Equal(
                "Cliente solicitou",
                cancelled.CancellationReason);
            Assert.Equal(CreatedAt, cancelled.CancelledAt);
            Assert.Equal(CreatedAt, cancelled.UpdatedAt);

            using var replayResponse = await client.PostAsJsonAsync(
                url,
                new
                {
                    cancelledBy = "CLIENT",
                    reason = "Cliente solicitou"
                });

            var replayBody = await replayResponse.Content
                .ReadAsStringAsync();

            Assert.True(
                replayResponse.StatusCode == HttpStatusCode.OK,
                $"Replay HTTP: {(int)replayResponse.StatusCode}" +
                Environment.NewLine +
                replayBody);

            var replay = await replayResponse.Content
                .ReadFromJsonAsync<CancelAppointmentResponse>();

            Assert.Equal(cancelled, replay);

            using var getResponse = await client.GetAsync(
                $"/api/v1/appointments/{created.Id}");

            var getBody = await getResponse.Content
                .ReadAsStringAsync();

            Assert.True(
                getResponse.StatusCode == HttpStatusCode.OK,
                $"Consulta HTTP: {(int)getResponse.StatusCode}" +
                Environment.NewLine +
                getBody);

            var current = await getResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(current);
            Assert.Equal("CANCELLED_BY_CLIENT", current.Status);
            Assert.Equal(created.StartsAt, current.StartsAt);
            Assert.Equal(created.EndsAt, current.EndsAt);
            Assert.Equal(created.TotalPrice, current.TotalPrice);
            Assert.Equal(
                created.TotalDurationMinutes,
                current.TotalDurationMinutes);
            Assert.Equal(
                created.Items.ToArray(),
                current.Items.ToArray());

            await using var context = CreateContext(data.TenantId);

            var persisted = await context.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == created.Id);

            Assert.Equal(
                AppointmentStatus.CancelledByClient,
                persisted.Status);
            Assert.False(persisted.OccupiesSchedule);
            Assert.Equal(
                "Cliente solicitou",
                persisted.CancellationReason);
            Assert.Equal(CreatedAt, persisted.CancelledAt);

            var cancellationRecord = await context.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync(entity =>
                    entity.Operation == "CancelAppointment");

            Assert.Equal(cancellationKey, cancellationRecord.Key);

            Assert.Equal(
                2,
                await context.IdempotencyRecords.CountAsync());
            Assert.Equal(
                1,
                await context.Appointments.CountAsync());
            Assert.Equal(
                1,
                await context.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointments_List_ShouldApplyResourceAndStatusFilters()
    {
        var data = new TestData();
        var pendingId = Guid.NewGuid();
        var expiredId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedAsync(data);

            await SeedListedAppointmentAsync(
                data,
                pendingId,
                startsAt);

            await SeedListedAppointmentAsync(
                data,
                expiredId,
                startsAt.AddHours(1));

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == expiredId);

                Assert.True(
                    appointment.Expire(CreatedAt.AddMinutes(10)));

                await context.SaveChangesAsync();
            }

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            var resourceFilters =
                $"locationId={data.LocationId}" +
                $"&professionalId={data.ProfessionalId}" +
                $"&customerId={data.CustomerId}";

            var cases = new (string Query, Guid[] ExpectedIds)[]
            {
                (
                    $"locationId={data.LocationId}",
                    [pendingId, expiredId]
                ),
                (
                    $"professionalId={data.ProfessionalId}",
                    [pendingId, expiredId]
                ),
                (
                    $"customerId={data.CustomerId}",
                    [pendingId, expiredId]
                ),
                (
                    "status=PENDING",
                    [pendingId]
                ),
                (
                    "status=EXPIRED",
                    [expiredId]
                ),
                (
                    "status=CONFIRMED",
                    []
                ),
                (
                    resourceFilters + "&status=PENDING",
                    [pendingId]
                ),
                (
                    resourceFilters + "&status=EXPIRED",
                    [expiredId]
                ),
                (
                    $"locationId={Guid.NewGuid()}" +
                    $"&professionalId={data.ProfessionalId}" +
                    $"&customerId={data.CustomerId}&status=PENDING",
                    []
                ),
                (
                    $"locationId={data.LocationId}" +
                    $"&professionalId={Guid.NewGuid()}" +
                    $"&customerId={data.CustomerId}&status=PENDING",
                    []
                ),
                (
                    $"locationId={data.LocationId}" +
                    $"&professionalId={data.ProfessionalId}" +
                    $"&customerId={Guid.NewGuid()}&status=PENDING",
                    []
                )
            };

            foreach (var testCase in cases)
            {
                using var response = await client.GetAsync(
                    $"/api/v1/appointments?{testCase.Query}");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var body = await response.Content
                    .ReadFromJsonAsync<GetAppointmentsResponse>();

                Assert.NotNull(body);
                Assert.Equal(
                    testCase.ExpectedIds.Length,
                    body.TotalCount);

                Assert.Equal(
                    testCase.ExpectedIds,
                    body.Items.Select(item => item.Id).ToArray());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointments_List_ShouldIsolateItemsAndCountByTenant()
    {
        var current = new TestData();
        var other = new TestData();

        var currentId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedAsync(current);
            await SeedAsync(other);

            await SeedListedAppointmentAsync(
                current,
                currentId,
                startsAt);

            await SeedListedAppointmentAsync(
                other,
                otherId,
                startsAt);

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                current.TenantId.ToString());

            using var response = await client.GetAsync(
                "/api/v1/appointments");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content
                .ReadFromJsonAsync<GetAppointmentsResponse>();

            Assert.NotNull(body);
            Assert.Equal(1, body.TotalCount);
            Assert.Equal(currentId, Assert.Single(body.Items).Id);

            // IDs de recursos do outro tenant não devem revelar registros.
            var foreignFilters = new[]
            {
                $"locationId={other.LocationId}",
                $"professionalId={other.ProfessionalId}",
                $"customerId={other.CustomerId}"
            };

            foreach (var filter in foreignFilters)
            {
                using var filteredResponse = await client.GetAsync(
                    $"/api/v1/appointments?{filter}");

                Assert.Equal(
                    HttpStatusCode.OK,
                    filteredResponse.StatusCode);

                var filteredBody = await filteredResponse.Content
                    .ReadFromJsonAsync<GetAppointmentsResponse>();

                Assert.NotNull(filteredBody);
                Assert.Empty(filteredBody.Items);
                Assert.Equal(0, filteredBody.TotalCount);
            }

            // A requisição do outro tenant deve enxergar seu próprio registro.
            using var otherClient = factory.CreateClient();

            otherClient.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                other.TenantId.ToString());

            using var otherResponse = await otherClient.GetAsync(
                "/api/v1/appointments");

            Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);

            var otherBody = await otherResponse.Content
                .ReadFromJsonAsync<GetAppointmentsResponse>();

            Assert.NotNull(otherBody);
            Assert.Equal(1, otherBody.TotalCount);
            Assert.Equal(otherId, Assert.Single(otherBody.Items).Id);
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task Appointments_ListWithoutRecords_ShouldReturnEmptyPage()
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            using var response = await client.GetAsync(
                "/api/v1/appointments");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content
                .ReadFromJsonAsync<GetAppointmentsResponse>();

            Assert.NotNull(body);
            Assert.Empty(body.Items);
            Assert.Equal(1, body.Page);
            Assert.Equal(50, body.PageSize);
            Assert.Equal(0, body.TotalCount);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointments_List_ShouldOrderPaginateAndReturnSnapshots()
    {
        var data = new TestData();

        var firstStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            // Insere fora da ordem cronológica para verificar o ORDER BY.
            await SeedListedAppointmentAsync(
                data,
                thirdId,
                firstStart.AddHours(2));

            await SeedListedAppointmentAsync(
                data,
                firstId,
                firstStart);

            await SeedListedAppointmentAsync(
                data,
                secondId,
                firstStart.AddHours(1));

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            using var firstResponse = await client.GetAsync(
                "/api/v1/appointments?page=1&pageSize=2");

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var firstPage = await firstResponse.Content
                .ReadFromJsonAsync<GetAppointmentsResponse>();

            Assert.NotNull(firstPage);
            Assert.Equal(1, firstPage.Page);
            Assert.Equal(2, firstPage.PageSize);
            Assert.Equal(3, firstPage.TotalCount);

            Assert.Equal(
                new[] { firstId, secondId },
                firstPage.Items.Select(item => item.Id).ToArray());

            var appointment = firstPage.Items[0];

            Assert.Equal(data.BusinessId, appointment.BusinessId);
            Assert.Equal(data.LocationId, appointment.LocationId);
            Assert.Equal(data.CustomerId, appointment.CustomerId);
            Assert.Equal(data.ProfessionalId, appointment.ProfessionalId);
            Assert.Equal(firstStart, appointment.StartsAt);
            Assert.Equal(firstStart.AddMinutes(45), appointment.EndsAt);
            Assert.Equal("PENDING", appointment.Status);
            Assert.Equal(65m, appointment.TotalPrice);
            Assert.Equal(45, appointment.TotalDurationMinutes);
            Assert.Equal(
                CreatedAt.AddMinutes(10),
                appointment.ReservationExpiresAt);
            Assert.Equal(CreatedAt, appointment.CreatedAt);
            Assert.Equal(CreatedAt, appointment.UpdatedAt);

            var item = Assert.Single(appointment.Items);

            Assert.Equal(data.ServiceId, item.ServiceId);
            Assert.Equal("Corte histórico", item.Name);
            Assert.Equal(65m, item.Price);
            Assert.Equal(45, item.DurationMinutes);

            using var secondResponse = await client.GetAsync(
                "/api/v1/appointments?page=2&pageSize=2");

            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

            var secondPage = await secondResponse.Content
                .ReadFromJsonAsync<GetAppointmentsResponse>();

            Assert.NotNull(secondPage);
            Assert.Equal(2, secondPage.Page);
            Assert.Equal(2, secondPage.PageSize);
            Assert.Equal(3, secondPage.TotalCount);
            Assert.Equal(thirdId, Assert.Single(secondPage.Items).Id);

            using var beyondResponse = await client.GetAsync(
                "/api/v1/appointments?page=3&pageSize=2");

            Assert.Equal(HttpStatusCode.OK, beyondResponse.StatusCode);

            var beyondPage = await beyondResponse.Content
                .ReadFromJsonAsync<GetAppointmentsResponse>();

            Assert.NotNull(beyondPage);
            Assert.Empty(beyondPage.Items);
            Assert.Equal(3, beyondPage.TotalCount);
            Assert.Equal(3, beyondPage.Page);
            Assert.Equal(2, beyondPage.PageSize);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

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

            var createBody = await createResponse.Content
                .ReadAsStringAsync();

            Assert.True(
                createResponse.StatusCode == HttpStatusCode.Created,
                $"Criação HTTP: {(int)createResponse.StatusCode}" +
                Environment.NewLine +
                createBody);

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
    [InlineData(500)]
    [InlineData(501)]
    public async Task Appointments_Cancel_ShouldValidateNormalizedReasonLength(
        int length)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await SeedListedAppointmentAsync(
                data,
                appointmentId,
                new DateTimeOffset(
                    2026, 10, 26, 12, 0, 0, TimeSpan.Zero));

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString());

            var normalizedReason = new string('a', length);

            using var response = await client.PostAsJsonAsync(
                $"/api/v1/appointments/{appointmentId}/cancel",
                new
                {
                    cancelledBy = "CLIENT",
                    reason = "  " + normalizedReason + "  "
                });

            Assert.Equal(
                length == 500
                    ? HttpStatusCode.OK
                    : HttpStatusCode.BadRequest,
                response.StatusCode);

            await using var context = CreateContext(data.TenantId);

            var persisted = await context.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            if (length == 500)
            {
                var body = await response.Content
                    .ReadFromJsonAsync<CancelAppointmentResponse>();

                Assert.NotNull(body);
                Assert.Equal(normalizedReason, body.CancellationReason);
                Assert.Equal(normalizedReason, persisted.CancellationReason);
                Assert.Equal(
                    AppointmentStatus.CancelledByClient,
                    persisted.Status);
                Assert.Equal(CreatedAt, persisted.CancelledAt);
            }
            else
            {
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Null(persisted.CancellationReason);
                Assert.Null(persisted.CancelledAt);
            }

            Assert.Equal(
                length == 500 ? 1 : 0,
                await context.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Appointments_Cancel_WithExpiredReservation_ShouldReturnConflict(
        bool alreadyExpired)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var reservationCreatedAt = CreatedAt.AddMinutes(-10);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = new Appointment(
                    appointmentId,
                    data.TenantId,
                    data.BusinessId,
                    data.LocationId,
                    data.CustomerId,
                    data.ProfessionalId,
                    new DateTimeOffset(
                        2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
                    reservationCreatedAt,
                    TimeSpan.FromMinutes(10),
                    [
                        new AppointmentItem(
                            Guid.NewGuid(),
                            data.TenantId,
                            appointmentId,
                            data.ServiceId,
                            "Corte",
                            65m,
                            45)
                    ]);

                if (alreadyExpired)
                {
                    Assert.True(appointment.Expire(CreatedAt));
                }

                context.Appointments.Add(appointment);

                await context.SaveChangesAsync();
            }

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString());

            using var response = await client.PostAsJsonAsync(
                $"/api/v1/appointments/{appointmentId}/cancel",
                new
                {
                    cancelledBy = "CLIENT",
                    reason = "Cliente solicitou"
                });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();

            using var problem = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal(
                "RESERVATION_EXPIRED",
                problem.RootElement.GetProperty("code").GetString());

            Assert.Equal(
                409,
                problem.RootElement.GetProperty("status").GetInt32());

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    AppointmentStatus.Expired,
                    persisted.Status);
                Assert.False(persisted.OccupiesSchedule);
                Assert.Null(persisted.CancellationReason);
                Assert.Null(persisted.CancelledAt);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(CreatedAt, persisted.ReservationExpiresAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
                Assert.Single(persisted.Items);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("CLIENT", "Outro motivo")]
    [InlineData("BUSINESS", "Motivo original")]
    public async Task Appointments_Cancel_WithSameKeyAndDifferentContent_ShouldReturnConflict(
        string cancelledBy,
        string reason)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();

        try
        {
            await SeedAsync(data);

            await SeedListedAppointmentAsync(
                data,
                appointmentId,
                new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                key);

            var url = $"/api/v1/appointments/{appointmentId}/cancel";

            using var firstResponse = await client.PostAsJsonAsync(
                url,
                new
                {
                    cancelledBy = "CLIENT",
                    reason = "Motivo original"
                });

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var original = await firstResponse.Content
                .ReadFromJsonAsync<CancelAppointmentResponse>();

            Assert.NotNull(original);

            using var conflictResponse = await client.PostAsJsonAsync(
                url,
                new
                {
                    cancelledBy,
                    reason
                });

            Assert.Equal(
                HttpStatusCode.Conflict,
                conflictResponse.StatusCode);

            var json = await conflictResponse.Content.ReadAsStringAsync();

            using var problem = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal(
                "IDEMPOTENCY_CONFLICT",
                problem.RootElement.GetProperty("code").GetString());

            Assert.Equal(
                409,
                problem.RootElement.GetProperty("status").GetInt32());

            await using var context = CreateContext(data.TenantId);

            var persisted = await context.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                AppointmentStatus.CancelledByClient,
                persisted.Status);
            Assert.Equal(
                "Motivo original",
                persisted.CancellationReason);
            Assert.Equal(original.CancelledAt, persisted.CancelledAt);
            Assert.Equal(original.UpdatedAt, persisted.UpdatedAt);

            Assert.Equal(
                1,
                await context.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Appointments_Cancel_WithInvisibleAppointment_ShouldReturnNotFound(
        bool belongsToAnotherTenant)
    {
        var current = new TestData();
        var other = new TestData();
        var appointmentId = Guid.NewGuid();

        try
        {
            await SeedAsync(current);
            await SeedAsync(other);

            if (belongsToAnotherTenant)
            {
                await SeedListedAppointmentAsync(
                    other,
                    appointmentId,
                    new DateTimeOffset(
                        2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
            }

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                current.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString());

            using var response = await client.PostAsJsonAsync(
                $"/api/v1/appointments/{appointmentId}/cancel",
                new
                {
                    cancelledBy = "CLIENT",
                    reason = "Cliente solicitou"
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using (var context = CreateContext(current.TenantId))
            {
                Assert.Equal(
                    0,
                    await context.Appointments.CountAsync());
                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }

            await using (var context = CreateContext(other.TenantId))
            {
                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());

                if (belongsToAnotherTenant)
                {
                    var appointment = await context.Appointments
                        .AsNoTracking()
                        .SingleAsync(entity => entity.Id == appointmentId);

                    Assert.Equal(
                        AppointmentStatus.Pending,
                        appointment.Status);
                    Assert.Null(appointment.CancellationReason);
                    Assert.Null(appointment.CancelledAt);
                    Assert.Equal(CreatedAt, appointment.UpdatedAt);
                }
                else
                {
                    Assert.Equal(
                        0,
                        await context.Appointments.CountAsync());
                }
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Theory]
    [InlineData("{}", "valid-key")]
    [InlineData(
        "{\"cancelledBy\":\"CLIENT\"}",
        "valid-key")]
    [InlineData(
        "{\"reason\":\"Motivo\"}",
        "valid-key")]
    [InlineData(
        "{\"cancelledBy\":\"INVALID\",\"reason\":\"Motivo\"}",
        "valid-key")]
    [InlineData(
        "{\"cancelledBy\":\"client\",\"reason\":\"Motivo\"}",
        "valid-key")]
    [InlineData(
        "{\"cancelledBy\":1,\"reason\":\"Motivo\"}",
        "valid-key")]
    [InlineData(
        "{\"cancelledBy\":\"CLIENT\",\"reason\":null}",
        "valid-key")]
    [InlineData(
        "{\"cancelledBy\":\"CLIENT\",\"reason\":\"\"}",
        "valid-key")]
    [InlineData(
        "{\"cancelledBy\":\"CLIENT\",\"reason\":\"   \"}",
        "valid-key")]
    [InlineData(
        "{\"cancelledBy\":\"CLIENT\",\"reason\":\"Motivo\"}",
        null)]
    [InlineData(
        "{\"cancelledBy\":\"CLIENT\",\"reason\":\"Motivo\"}",
        "key with space")]
    [InlineData(
        "{\"cancelledBy\":\"CLIENT\",\"reason\":\"Motivo\"}",
        "too-long")]
    public async Task Appointments_Cancel_WithInvalidRequest_ShouldReturnBadRequest(
        string json,
        string? key)
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            if (key is not null)
            {
                client.DefaultRequestHeaders.Add(
                    "Idempotency-Key",
                    key == "too-long"
                        ? new string('a', 201)
                        : key);
            }

            using var content = new StringContent(
                json,
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await client.PostAsync(
                $"/api/v1/appointments/{Guid.NewGuid()}/cancel",
                content);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            await using var context = CreateContext(data.TenantId);

            Assert.Equal(
                0,
                await context.IdempotencyRecords.CountAsync());
            Assert.Equal(
                0,
                await context.Appointments.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("CLIENT", "CANCELLED_BY_CLIENT")]
    [InlineData("BUSINESS", "CANCELLED_BY_BUSINESS")]
    public async Task Appointments_Cancel_ShouldAllowNewBookingInReleasedInterval(
        string cancelledBy,
        string expectedStatus)
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString());

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

            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString());

            using var cancelResponse = await client.PostAsJsonAsync(
                $"/api/v1/appointments/{created.Id}/cancel",
                new
                {
                    cancelledBy,
                    reason = "Cancelamento solicitado"
                });

            Assert.Equal(
                HttpStatusCode.OK,
                cancelResponse.StatusCode);

            var cancelled = await cancelResponse.Content
                .ReadFromJsonAsync<CancelAppointmentResponse>();

            Assert.NotNull(cancelled);
            Assert.Equal(expectedStatus, cancelled.Status);

            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString());

            using var replacementResponse = await client.PostAsJsonAsync(
                "/api/v1/appointments",
                payload);

            var replacementBody = await replacementResponse.Content
                .ReadAsStringAsync();

            Assert.True(
                replacementResponse.StatusCode == HttpStatusCode.Created,
                $"Nova reserva HTTP: {(int)replacementResponse.StatusCode}" +
                Environment.NewLine +
                replacementBody);

            var replacement = await replacementResponse.Content
                .ReadFromJsonAsync<AppointmentResponse>();

            Assert.NotNull(replacement);
            Assert.NotEqual(created.Id, replacement.Id);
            Assert.Equal("PENDING", replacement.Status);
            Assert.Equal(created.StartsAt, replacement.StartsAt);
            Assert.Equal(created.EndsAt, replacement.EndsAt);

            await using var context = CreateContext(data.TenantId);

            Assert.Equal(2, await context.Appointments.CountAsync());
            Assert.Equal(2, await context.AppointmentItems.CountAsync());
            Assert.Equal(3, await context.IdempotencyRecords.CountAsync());

            var original = await context.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == created.Id);

            Assert.False(original.OccupiesSchedule);
            Assert.Equal(
                "Cancelamento solicitado",
                original.CancellationReason);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("locationId=invalid")]
    [InlineData("professionalId=invalid")]
    [InlineData("customerId=invalid")]
    [InlineData("locationId=00000000-0000-0000-0000-000000000000")]
    [InlineData("professionalId=00000000-0000-0000-0000-000000000000")]
    [InlineData("customerId=00000000-0000-0000-0000-000000000000")]
    [InlineData("status=INVALID")]
    [InlineData("status=1")]
    [InlineData("status=Pending")]
    [InlineData("status=")]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=invalid")]
    [InlineData("page=2147483648")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=invalid")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("from=invalid&to=2026-10-20T00:00:00Z")]
    [InlineData("from=2026-10-19T00:00:00Z&to=invalid")]
    [InlineData("from=2026-10-19T00:00:00Z")]
    [InlineData("to=2026-10-20T00:00:00Z")]
    [InlineData("from=2026-10-19T12:00:00Z&to=2026-10-19T12:00:00Z")]
    [InlineData("from=2026-10-20T00:00:00Z&to=2026-10-19T00:00:00Z")]
    public async Task Appointments_List_WithInvalidQuery_ShouldReturnBadRequest(
        string query)
    {
        var data = new TestData();

        try
        {
            await SeedAsync(data);

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            using var response = await client.GetAsync(
                $"/api/v1/appointments?{query}");

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(
        "2026-10-19T12:15:00Z",
        "2026-10-19T12:30:00Z",
        1)]
    [InlineData(
        "2026-10-19T11:00:00Z",
        "2026-10-19T13:00:00Z",
        1)]
    [InlineData(
        "2026-10-19T12:00:00Z",
        "2026-10-19T12:45:00Z",
        1)]
    [InlineData(
        "2026-10-19T11:00:00Z",
        "2026-10-19T12:00:00Z",
        0)]
    [InlineData(
        "2026-10-19T12:45:00Z",
        "2026-10-19T13:00:00Z",
        0)]
    [InlineData(
        "2026-10-19T09:15:00-03:00",
        "2026-10-19T09:30:00-03:00",
        1)]
    public async Task Appointments_List_WithPeriod_ShouldUseOverlap(
        string from,
        string to,
        int expectedCount)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await SeedListedAppointmentAsync(
                data,
                appointmentId,
                new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                data.TenantId.ToString());

            var url =
                "/api/v1/appointments" +
                $"?from={Uri.EscapeDataString(from)}" +
                $"&to={Uri.EscapeDataString(to)}";

            using var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content
                .ReadFromJsonAsync<GetAppointmentsResponse>();

            Assert.NotNull(body);
            Assert.Equal(expectedCount, body.TotalCount);
            Assert.Equal(expectedCount, body.Items.Count);

            if (expectedCount == 1)
            {
                Assert.Equal(
                    appointmentId,
                    Assert.Single(body.Items).Id);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

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

    private async Task SeedListedAppointmentAsync(TestData data, Guid appointmentId, DateTimeOffset startsAt)
    {
        await using var context = CreateContext(data.TenantId);

        var appointment = new Appointment(
            appointmentId,
            data.TenantId,
            data.BusinessId,
            data.LocationId,
            data.CustomerId,
            data.ProfessionalId,
            startsAt,
            CreatedAt,
            TimeSpan.FromMinutes(10),
            [
                new AppointmentItem(
                    Guid.NewGuid(),
                    data.TenantId,
                    appointmentId,
                    data.ServiceId,
                    "Corte histórico",
                    65m,
                    45)
            ]);

        context.Appointments.Add(appointment);

        await context.SaveChangesAsync();
    }
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