using Microsoft.EntityFrameworkCore;
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
using Microsoft.Extensions.Options;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Infrastructure.Appointments;
using VirtualEmployee.Application.Appointments.CancelAppointment;
using VirtualEmployee.Application.Appointments.RescheduleAppointment;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Scheduling;

[Collection(PostgreSqlCollection.Name)]
public sealed class AppointmentPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public AppointmentPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task AppointmentReschedulingService_WithForeignAppointment_ShouldReturnNotFound()
    {
        var current = new TestData();
        var other = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedBookableAsync(current);
            await SeedBookableAsync(other);

            await using (var context = CreateContext(other.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        other,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                current.LocationId,
                current.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(current.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(
                            CreatedAt.AddMinutes(5))));

                var result = await handler.HandleAsync(command);

                Assert.Equal(
                    RescheduleAppointmentError.AppointmentNotFound,
                    result.Error);
                Assert.Null(result.Response);

                Assert.Equal(
                    0,
                    await context.Appointments.CountAsync());
                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }

            await using (var context = CreateContext(other.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(other.LocationId, persisted.LocationId);
                Assert.Equal(
                    other.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(originalStart, persisted.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    persisted.EndsAt);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
                Assert.Single(persisted.Items);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
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

    [Fact]
    public async Task AppointmentReschedulingService_WithConcurrentSameKey_ShouldReplayWinner()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    secondContext,
                    new FixedTimeProvider(now.AddSeconds(1))));

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            async Task<RescheduleAppointmentResult> ExecuteAsync(
                RescheduleAppointmentHandler handler)
            {
                await start.Task.WaitAsync(timeout.Token);

                return await handler.HandleAsync(
                    command,
                    timeout.Token);
            }

            var firstTask = ExecuteAsync(firstHandler);
            var secondTask = ExecuteAsync(secondHandler);

            start.SetResult(true);

            var results = await Task.WhenAll(
                firstTask,
                secondTask);

            Assert.All(
                results,
                result => Assert.True(
                    result.IsSuccess,
                    $"Erro de reagendamento: {result.Error}"));

            var firstResponse = Assert.IsType<AppointmentResponse>(
                results[0].Response);

            var secondResponse = Assert.IsType<AppointmentResponse>(
                results[1].Response);

            Assert.Equal(
                firstResponse with
                {
                    Items = secondResponse.Items
                },
                secondResponse);

            Assert.Equal(
                firstResponse.Items.ToArray(),
                secondResponse.Items.ToArray());

            Assert.Equal(command.StartsAt, firstResponse.StartsAt);

            Assert.Contains(
                firstResponse.UpdatedAt,
                new[] { now, now.AddSeconds(1) });

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(command.StartsAt, persisted.StartsAt);
            Assert.Equal(
                command.StartsAt.AddMinutes(45),
                persisted.EndsAt);
            Assert.Equal(
                firstResponse.UpdatedAt,
                persisted.UpdatedAt);
            Assert.Equal(AppointmentStatus.Pending, persisted.Status);
            Assert.Equal(
                CreatedAt.AddMinutes(10),
                persisted.ReservationExpiresAt);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("RescheduleAppointment", record.Operation);
            Assert.Equal(command.IdempotencyKey, record.Key);
            Assert.Equal(firstResponse.UpdatedAt, record.CreatedAt);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<AppointmentResponse>(
                        record.ResponseJson);

            Assert.NotNull(storedResponse);
            Assert.Equal(firstResponse.StartsAt, storedResponse.StartsAt);
            Assert.Equal(firstResponse.UpdatedAt, storedResponse.UpdatedAt);
            Assert.Equal(
                firstResponse.Items.ToArray(),
                storedResponse.Items.ToArray());

            Assert.Equal(
                1,
                await readContext.Appointments.CountAsync());
            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_WithConcurrentDifferentDestinations_ShouldPersistOnlyOne()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var firstCommand = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            var secondCommand = firstCommand with
            {
                StartsAt = originalStart.AddHours(2),
                IdempotencyKey = Guid.NewGuid().ToString()
            };

            var gate = new ConcurrentSaveGate();

            await using var firstContext = CreateContext(
                data.TenantId,
                gate);

            await using var secondContext = CreateContext(
                data.TenantId,
                gate);

            var firstHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    secondContext,
                    new FixedTimeProvider(now)));

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            var results = await Task.WhenAll(
                firstHandler.HandleAsync(
                    firstCommand,
                    timeout.Token),
                secondHandler.HandleAsync(
                    secondCommand,
                    timeout.Token));

            Assert.Equal(2, gate.Arrivals);

            var winner = Assert.Single(
                results,
                result => result.IsSuccess);

            var loser = Assert.Single(
                results,
                result => !result.IsSuccess);

            Assert.Equal(
                RescheduleAppointmentError.ConcurrencyConflict,
                loser.Error);
            Assert.Null(loser.Response);

            var winningCommand = results[0].IsSuccess
                ? firstCommand
                : secondCommand;

            var losingContext = results[0].IsSuccess
                ? secondContext
                : firstContext;

            Assert.Empty(losingContext.ChangeTracker.Entries());

            var response = Assert.IsType<AppointmentResponse>(
                winner.Response);

            Assert.Equal(winningCommand.StartsAt, response.StartsAt);

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                winningCommand.StartsAt,
                persisted.StartsAt);
            Assert.Equal(
                winningCommand.StartsAt.AddMinutes(45),
                persisted.EndsAt);
            Assert.Equal(AppointmentStatus.Pending, persisted.Status);
            Assert.Equal(now, persisted.UpdatedAt);
            Assert.Equal(
                CreatedAt.AddMinutes(10),
                persisted.ReservationExpiresAt);
            Assert.Equal(65m, persisted.TotalPriceSnapshot);
            Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
            Assert.Single(persisted.Items);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("RescheduleAppointment", record.Operation);
            Assert.Equal(winningCommand.IdempotencyKey, record.Key);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<AppointmentResponse>(
                        record.ResponseJson);

            Assert.NotNull(storedResponse);
            Assert.Equal(response.StartsAt, storedResponse.StartsAt);
            Assert.Equal(response.EndsAt, storedResponse.EndsAt);

            Assert.Equal(
                1,
                await readContext.Appointments.CountAsync());
            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_ShouldChangeLocationAndProfessional()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var destinationLocationId = Guid.NewGuid();
        var destinationProfessionalId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Locations.Add(
                    new Location(
                        destinationLocationId,
                        data.TenantId,
                        data.BusinessId,
                        "Unidade de destino",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.Professionals.Add(
                    new Professional(
                        destinationProfessionalId,
                        data.TenantId,
                        data.BusinessId,
                        "Profissional de destino",
                        CreatedAt));

                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        startsAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        data.TenantId,
                        destinationProfessionalId,
                        destinationLocationId,
                        CreatedAt));

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        data.TenantId,
                        destinationProfessionalId,
                        data.ServiceId,
                        CreatedAt));

                context.LocationServices.Add(
                    new LocationService(
                        data.TenantId,
                        destinationLocationId,
                        data.ServiceId,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        Guid.NewGuid(),
                        data.TenantId,
                        destinationLocationId,
                        destinationProfessionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(18, 0),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                destinationLocationId,
                destinationProfessionalId,
                startsAt,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.True(
                    result.IsSuccess,
                    $"Erro de reagendamento: {result.Error}");

                var response = Assert.IsType<AppointmentResponse>(
                    result.Response);

                Assert.Equal(appointmentId, response.Id);
                Assert.Equal(
                    destinationLocationId,
                    response.LocationId);
                Assert.Equal(
                    destinationProfessionalId,
                    response.ProfessionalId);
                Assert.Equal(data.BusinessId, response.BusinessId);
                Assert.Equal(data.CustomerId, response.CustomerId);
                Assert.Equal(startsAt, response.StartsAt);
                Assert.Equal(startsAt.AddMinutes(45), response.EndsAt);
                Assert.Equal(65m, response.TotalPrice);
                Assert.Equal(45, response.TotalDurationMinutes);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    destinationLocationId,
                    persisted.LocationId);
                Assert.Equal(
                    destinationProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);

                var item = Assert.Single(persisted.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.ServiceNameSnapshot);
                Assert.Equal(65m, item.PriceSnapshot);
                Assert.Equal(45, item.DurationMinutesSnapshot);

                Assert.Equal(
                    1,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    1,
                    await context.Appointments.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_ShouldReplayWithEquivalentOffsetAfterDeadline()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddHours(1);
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            AppointmentResponse originalResponse;

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);

                originalResponse = Assert.IsType<AppointmentResponse>(
                    result.Response);
            }

            // Simula a expiração após o reagendamento.
            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.True(
                    appointment.Expire(CreatedAt.AddMinutes(10)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(newStart.AddHours(1))));

                var replay = await handler.HandleAsync(
                    command with
                    {
                        StartsAt = newStart.ToOffset(
                            TimeSpan.FromHours(-3))
                    });

                Assert.True(replay.IsSuccess);

                var response = Assert.IsType<AppointmentResponse>(
                    replay.Response);

                // Compara os itens por conteúdo, pois o replay
                // desserializa uma nova coleção.
                Assert.Equal(
                    originalResponse with
                    {
                        Items = response.Items
                    },
                    response);

                Assert.Equal(
                    originalResponse.Items.ToArray(),
                    response.Items.ToArray());

                Assert.Equal("PENDING", response.Status);
                Assert.Equal(now, response.UpdatedAt);
                Assert.Equal(newStart, response.StartsAt);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                // Replay não reativa nem altera o estado atual.
                Assert.Equal(
                    AppointmentStatus.Expired,
                    persisted.Status);
                Assert.Equal(newStart, persisted.StartsAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.UpdatedAt);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal("RescheduleAppointment", record.Operation);
                Assert.Equal(command.IdempotencyKey, record.Key);
                Assert.Equal(now, record.CreatedAt);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_WithOccupiedDestination_ShouldPreserveOriginalBooking()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var occupyingId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var occupiedStart = originalStart.AddHours(1);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        occupyingId,
                        occupiedStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                occupiedStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(
                            CreatedAt.AddMinutes(5))));

                var result = await handler.HandleAsync(command);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    RescheduleAppointmentError.SlotUnavailable,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var original = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(originalStart, original.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    original.EndsAt);
                Assert.Equal(data.LocationId, original.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    original.ProfessionalId);
                Assert.Equal(AppointmentStatus.Pending, original.Status);
                Assert.True(original.OccupiesSchedule);
                Assert.Equal(CreatedAt, original.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    original.ReservationExpiresAt);
                Assert.Equal(65m, original.TotalPriceSnapshot);
                Assert.Equal(45, original.TotalDurationMinutesSnapshot);
                Assert.Single(original.Items);

                var occupying = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == occupyingId);

                Assert.Equal(occupiedStart, occupying.StartsAt);
                Assert.Equal(
                    AppointmentStatus.Pending,
                    occupying.Status);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    2,
                    await context.Appointments.CountAsync());
                Assert.Equal(
                    2,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_ShouldIgnoreOwnIntervalAndPreserveSnapshots()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddMinutes(15);
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            Guid originalItemId;

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = CreateAppointment(
                    data,
                    appointmentId,
                    originalStart);

                originalItemId = Assert.Single(appointment.Items).Id;

                context.Appointments.Add(appointment);

                await context.SaveChangesAsync();
            }

            // O catálogo muda, mas os valores do agendamento permanecem.
            await using (var context = CreateContext(data.TenantId))
            {
                var service = await context.Services
                    .SingleAsync(entity => entity.Id == data.ServiceId);

                service.Update(
                    "Corte atualizado",
                    90m,
                    60,
                    true,
                    now);

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.True(
                    result.IsSuccess,
                    $"Erro de reagendamento: {result.Error}");

                var response = Assert.IsType<AppointmentResponse>(
                    result.Response);

                Assert.Equal(appointmentId, response.Id);
                Assert.Equal(newStart, response.StartsAt);
                Assert.Equal(newStart.AddMinutes(45), response.EndsAt);
                Assert.Equal("PENDING", response.Status);
                Assert.Equal(65m, response.TotalPrice);
                Assert.Equal(45, response.TotalDurationMinutes);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    response.ReservationExpiresAt);

                var item = Assert.Single(response.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.Name);
                Assert.Equal(65m, item.Price);
                Assert.Equal(45, item.DurationMinutes);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(data.ProfessionalId, persisted.ProfessionalId);
                Assert.Equal(data.CustomerId, persisted.CustomerId);
                Assert.Equal(data.BusinessId, persisted.BusinessId);

                Assert.Equal(newStart, persisted.StartsAt);
                Assert.Equal(newStart.AddMinutes(45), persisted.EndsAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);

                var item = Assert.Single(persisted.Items);

                Assert.Equal(originalItemId, item.Id);
                Assert.Equal("Corte", item.ServiceNameSnapshot);
                Assert.Equal(65m, item.PriceSnapshot);
                Assert.Equal(45, item.DurationMinutesSnapshot);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal("RescheduleAppointment", record.Operation);
                Assert.Equal(command.IdempotencyKey, record.Key);

                var storedResponse =
                    System.Text.Json.JsonSerializer
                        .Deserialize<AppointmentResponse>(
                            record.ResponseJson);

                Assert.NotNull(storedResponse);
                Assert.Equal(appointmentId, storedResponse.Id);
                Assert.Equal(newStart, storedResponse.StartsAt);
                Assert.Equal(65m, storedResponse.TotalPrice);
                Assert.Equal(45, storedResponse.TotalDurationMinutes);

                Assert.Equal(1, await context.Appointments.CountAsync());
                Assert.Equal(1, await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_WhenExpirationWins_ShouldNotOverwriteExpiredState()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var expiresAt = CreatedAt.AddMinutes(10);
        var expiredCount = 0;

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var interceptor = new BeforeCancellationSaveInterceptor(
                async () =>
                {
                    await using var expirationContext =
                        CreateContext(data.TenantId);

                    var expirationService =
                        new AppointmentExpirationService(
                            expirationContext);

                    expiredCount = await expirationService.ExpireDueAsync(
                        expiresAt);
                });

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(
                data.TenantId,
                interceptor))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(
                            expiresAt.AddSeconds(-1))));

                var result = await handler.HandleAsync(command);

                Assert.Equal(1, expiredCount);
                Assert.Equal(
                    CancelAppointmentError.ConcurrencyConflict,
                    result.Error);
                Assert.Null(result.Response);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    AppointmentStatus.Expired,
                    appointment.Status);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Null(appointment.CancelledAt);
                Assert.Null(appointment.CancellationReason);
                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Single(appointment.Items);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }

            // Uma nova tentativa observa o estado vencedor.
            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(expiresAt.AddSeconds(1))));

                var retry = await handler.HandleAsync(command);

                Assert.Equal(
                    CancelAppointmentError.ReservationExpired,
                    retry.Error);
                Assert.Null(retry.Response);
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

    [Fact]
    public async Task AppointmentCancellationService_WithConcurrentDifferentKeys_ShouldPersistOnlyOne()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var firstCommand = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            var secondCommand = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByBusiness,
                "Estabelecimento solicitou",
                Guid.NewGuid().ToString());

            var gate = new ConcurrentSaveGate();

            await using var firstContext = CreateContext(
                data.TenantId,
                gate);

            await using var secondContext = CreateContext(
                data.TenantId,
                gate);

            var firstHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    secondContext,
                    new FixedTimeProvider(now)));

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            var results = await Task.WhenAll(
                firstHandler.HandleAsync(
                    firstCommand,
                    timeout.Token),
                secondHandler.HandleAsync(
                    secondCommand,
                    timeout.Token));

            Assert.Equal(2, gate.Arrivals);

            var winner = Assert.Single(
                results,
                result => result.IsSuccess);

            var loser = Assert.Single(
                results,
                result => !result.IsSuccess);

            Assert.Equal(
                CancelAppointmentError.ConcurrencyConflict,
                loser.Error);
            Assert.Null(loser.Response);

            var response = Assert.IsType<CancelAppointmentResponse>(
                winner.Response);

            var firstWon = results[0].IsSuccess;
            var winningCommand = firstWon
                ? firstCommand
                : secondCommand;

            var losingContext = firstWon
                ? secondContext
                : firstContext;

            // A tentativa revertida não deve deixar alterações pendentes.
            Assert.Empty(losingContext.ChangeTracker.Entries());

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                winningCommand.CancellationStatus,
                persisted.Status);
            Assert.Equal(
                winningCommand.Reason,
                persisted.CancellationReason);
            Assert.Equal(response.CancelledAt, persisted.CancelledAt);
            Assert.Equal(now, persisted.UpdatedAt);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("CancelAppointment", record.Operation);
            Assert.Equal(winningCommand.IdempotencyKey, record.Key);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<CancelAppointmentResponse>(
                        record.ResponseJson);

            Assert.Equal(response, storedResponse);
            Assert.Equal(1, await readContext.Appointments.CountAsync());
            Assert.Equal(1, await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_WithConcurrentSameKey_ShouldReplayWinner()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    secondContext,
                    new FixedTimeProvider(now.AddSeconds(1))));

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            async Task<CancelAppointmentResult> ExecuteAsync(
                CancelAppointmentHandler handler)
            {
                await start.Task.WaitAsync(timeout.Token);

                return await handler.HandleAsync(
                    command,
                    timeout.Token);
            }

            var firstTask = ExecuteAsync(firstHandler);
            var secondTask = ExecuteAsync(secondHandler);

            start.SetResult(true);

            var results = await Task.WhenAll(
                firstTask,
                secondTask);

            Assert.All(results, result => Assert.True(result.IsSuccess));

            var firstResponse = Assert.IsType<CancelAppointmentResponse>(
                results[0].Response);

            var secondResponse = Assert.IsType<CancelAppointmentResponse>(
                results[1].Response);

            Assert.Equal(firstResponse, secondResponse);
            Assert.Equal(appointmentId, firstResponse.Id);

            Assert.Contains(
                firstResponse.CancelledAt,
                new[] { now, now.AddSeconds(1) });

            await using var readContext = CreateContext(data.TenantId);

            var appointment = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                AppointmentStatus.CancelledByClient,
                appointment.Status);
            Assert.Equal(
                firstResponse.CancelledAt,
                appointment.CancelledAt);
            Assert.Equal(
                firstResponse.UpdatedAt,
                appointment.UpdatedAt);
            Assert.Equal("Cliente solicitou", appointment.CancellationReason);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("CancelAppointment", record.Operation);
            Assert.Equal(command.IdempotencyKey, record.Key);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<CancelAppointmentResponse>(
                        record.ResponseJson);

            Assert.Equal(firstResponse, storedResponse);
            Assert.Equal(1, await readContext.Appointments.CountAsync());
            Assert.Equal(1, await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_ShouldIsolateAppointmentAndKeyByTenant()
    {
        var current = new TestData();
        var other = new TestData();

        var currentId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(current);
            await SeedAsync(other);

            await using (var context = CreateContext(current.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        current,
                        currentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(other.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        other,
                        otherId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var currentCommand = new CancelAppointmentCommand(
                currentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                key);

            await using (var context = CreateContext(current.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                Assert.True(
                    (await handler.HandleAsync(currentCommand)).IsSuccess);
            }

            // A chave existente no primeiro tenant não deve gerar replay
            // nem conflito ao consultar um agendamento invisível no segundo.
            await using (var context = CreateContext(other.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(currentCommand);

                Assert.Equal(
                    CancelAppointmentError.AppointmentNotFound,
                    result.Error);
                Assert.Null(result.Response);
                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());

                var untouched = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(otherId, untouched.Id);
                Assert.Equal(AppointmentStatus.Pending, untouched.Status);
                Assert.Null(untouched.CancelledAt);
            }

            // A mesma chave pode ser usada pelo outro tenant
            // para cancelar seu próprio agendamento.
            await using (var context = CreateContext(other.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(
                    currentCommand with
                    {
                        AppointmentId = otherId
                    });

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);
                Assert.Equal(otherId, result.Response.Id);
            }

            foreach (var data in new[] { current, other })
            {
                await using var context = CreateContext(data.TenantId);

                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(
                    AppointmentStatus.CancelledByClient,
                    appointment.Status);
                Assert.Equal(now, appointment.CancelledAt);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(data.TenantId, record.TenantId);
                Assert.Equal("CancelAppointment", record.Operation);
                Assert.Equal(key, record.Key);
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

    [Fact]
    public async Task AppointmentCancellationService_ShouldPersistAndReplayOriginalResponse()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var cancelledAt = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "  Cliente solicitou  ",
                key);

            CancelAppointmentResponse original;

            await using (var context = CreateContext(data.TenantId))
            {
                var service = new AppointmentCancellationService(
                    context,
                    new FixedTimeProvider(cancelledAt));

                var handler = new CancelAppointmentHandler(service);

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);

                original = Assert.IsType<CancelAppointmentResponse>(
                    result.Response);

                Assert.Equal(appointmentId, original.Id);
                Assert.Equal("CANCELLED_BY_CLIENT", original.Status);
                Assert.Equal(
                    "Cliente solicitou",
                    original.CancellationReason);
                Assert.Equal(cancelledAt, original.CancelledAt);
                Assert.Equal(cancelledAt, original.UpdatedAt);
            }

            // O replay ocorre depois do prazo original da reserva.
            await using (var context = CreateContext(data.TenantId))
            {
                var service = new AppointmentCancellationService(
                    context,
                    new FixedTimeProvider(CreatedAt.AddHours(3)));

                var handler = new CancelAppointmentHandler(service);

                var replay = await handler.HandleAsync(
                    command with
                    {
                        Reason = "Cliente solicitou"
                    });

                Assert.True(replay.IsSuccess);
                Assert.Equal(original, replay.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    AppointmentStatus.CancelledByClient,
                    appointment.Status);
                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(
                    "Cliente solicitou",
                    appointment.CancellationReason);
                Assert.Equal(cancelledAt, appointment.CancelledAt);
                Assert.Equal(cancelledAt, appointment.UpdatedAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal("CancelAppointment", record.Operation);
                Assert.Equal(key, record.Key);
                Assert.Equal(cancelledAt, record.CreatedAt);
                Assert.False(string.IsNullOrWhiteSpace(record.RequestHash));

                var storedResponse =
                    System.Text.Json.JsonSerializer
                        .Deserialize<CancelAppointmentResponse>(
                            record.ResponseJson);

                Assert.Equal(original, storedResponse);
                Assert.Equal(1, await context.Appointments.CountAsync());
                Assert.Equal(1, await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentExpiration_ShouldRespectDeadlineTenantAndIdempotency()
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentAId = Guid.NewGuid();
        var appointmentBId = Guid.NewGuid();

        var expiresAt = CreatedAt.AddMinutes(10);
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataA.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataA,
                        appointmentAId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataB,
                        appointmentBId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var beforeDeadline = await service.ExpireDueAsync(
                    expiresAt.AddSeconds(-1));

                Assert.Equal(0, beforeDeadline);
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var atDeadline = await service.ExpireDueAsync(
                    expiresAt.ToOffset(TimeSpan.FromHours(-3)));

                Assert.Equal(1, atDeadline);
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var repeated = await service.ExpireDueAsync(
                    expiresAt.AddMinutes(5));

                Assert.Equal(0, repeated);
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity =>
                        entity.Id == appointmentAId);

                Assert.Equal(
                    AppointmentStatus.Expired,
                    appointment.Status);

                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Equal(CreatedAt, appointment.CreatedAt);

                Assert.Equal<DateTimeOffset?>(
                    expiresAt,
                    appointment.ReservationExpiresAt);

                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity =>
                        entity.Id == appointmentBId);

                // A execução do tenant A não altera o tenant B.
                Assert.Equal(
                    AppointmentStatus.Pending,
                    appointment.Status);

                Assert.Equal(CreatedAt, appointment.UpdatedAt);

                var service = new AppointmentExpirationService(context);

                Assert.Equal(
                    1,
                    await service.ExpireDueAsync(expiresAt));
            }
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_WithConcurrentDifferentKeys_ShouldPersistOnlyOne()
    {
        var data = new TestData();

        var firstCommand = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            "appointment-concurrent-first");

        var secondCommand = firstCommand with
        {
            IdempotencyKey = "appointment-concurrent-second"
        };

        try
        {
            await SeedBookableAsync(data);

            var gate = new ConcurrentSaveGate();

            await using var firstContext =
                CreateContext(data.TenantId, gate);

            await using var secondContext =
                CreateContext(data.TenantId, gate);

            var firstTask = CreateHandler(firstContext)
                .HandleAsync(firstCommand);

            var secondTask = CreateHandler(secondContext)
                .HandleAsync(secondCommand);

            var results = await Task.WhenAll(firstTask, secondTask)
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(2, gate.Arrivals);

            var winner = Assert.Single(
                results,
                result => result.IsSuccess);

            var loser = Assert.Single(
                results,
                result => !result.IsSuccess);

            Assert.NotNull(winner.Response);
            Assert.Equal(
                CreateAppointmentError.SlotUnavailable,
                loser.Error);
            Assert.Null(loser.Response);

            var winnerCommand = results[0].IsSuccess
                ? firstCommand
                : secondCommand;

            var loserCommand = results[0].IsSuccess
                ? secondCommand
                : firstCommand;

            await using var readContext = CreateContext(data.TenantId);

            var appointment = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(winner.Response.Id, appointment.Id);

            var item = await readContext.AppointmentItems
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(appointment.Id, item.AppointmentId);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(winnerCommand.IdempotencyKey, record.Key);
            Assert.Equal(
                AppointmentRequestHasher.Compute(winnerCommand),
                record.RequestHash);

            Assert.False(
                await readContext.IdempotencyRecords
                    .AnyAsync(entity =>
                        entity.Operation == "CreateAppointment" &&
                        entity.Key == loserCommand.IdempotencyKey));

            // A tentativa perdedora não deixa entidades pendentes no contexto.
            var losingContext = results[0].IsSuccess
                ? secondContext
                : firstContext;

            Assert.Empty(
                losingContext.ChangeTracker.Entries<Appointment>());

            Assert.Empty(
                losingContext.ChangeTracker.Entries<AppointmentItem>());

            Assert.Empty(
                losingContext.ChangeTracker.Entries<
                    VirtualEmployee.Infrastructure.Persistence
                        .Idempotency.IdempotencyRecord>());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_WithConcurrentSameKey_ShouldReplayWinner()
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3)),
            "appointment-concurrent-same-key");

        try
        {
            await SeedBookableAsync(data);

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstHandler = CreateHandler(firstContext);
            var secondHandler = CreateHandler(secondContext);

            var startGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<CreateAppointmentResult> ExecuteAsync(
                CreateAppointmentHandler handler,
                CreateAppointmentCommand request)
            {
                await startGate.Task;

                return await handler.HandleAsync(request);
            }

            var firstTask = ExecuteAsync(firstHandler, command);

            var secondTask = ExecuteAsync(
                secondHandler,
                command with
                {
                    StartsAt = command.StartsAt.ToUniversalTime()
                });

            startGate.SetResult(true);

            var results = await Task.WhenAll(firstTask, secondTask)
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.All(results, result =>
            {
                Assert.True(result.IsSuccess);
                Assert.Equal(CreateAppointmentError.None, result.Error);
                Assert.NotNull(result.Response);
            });

            var first = results[0].Response!;
            var second = results[1].Response!;

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(first.StartsAt, second.StartsAt);
            Assert.Equal(first.EndsAt, second.EndsAt);
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.TotalPrice, second.TotalPrice);
            Assert.Equal(
                first.TotalDurationMinutes,
                second.TotalDurationMinutes);
            Assert.Equal(
                first.ReservationExpiresAt,
                second.ReservationExpiresAt);
            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.Equal(first.UpdatedAt, second.UpdatedAt);
            Assert.Equal(
                first.Items.ToArray(),
                second.Items.ToArray());

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(first.Id, persisted.Id);

            var item = await readContext.AppointmentItems
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(first.Id, item.AppointmentId);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(command.IdempotencyKey, record.Key);
            Assert.Equal(
                AppointmentRequestHasher.Compute(command),
                record.RequestHash);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_ShouldReplayOriginalResponseAfterChanges()
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3)),
            "appointment-replay-test");

        try
        {
            await SeedBookableAsync(data);

            AppointmentResponse original;

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(command);

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);

                original = result.Response;
            }

            var expiredAt = CreatedAt.AddMinutes(10);

            await using (var context = CreateContext(data.TenantId))
            {
                var service = await context.Services
                    .SingleAsync(entity => entity.Id == data.ServiceId);

                service.Update(
                    "Corte alterado",
                    99m,
                    60,
                    false,
                    CreatedAt.AddHours(1));

                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == original.Id);

                Assert.True(appointment.Expire(expiredAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                // Mesmo instante, agora representado com offset UTC.
                var replayCommand = command with
                {
                    StartsAt = command.StartsAt.ToUniversalTime()
                };

                var result = await CreateHandler(
                        context,
                        now: command.StartsAt.AddDays(1))
                    .HandleAsync(replayCommand);

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);

                var replay = result.Response;

                Assert.Equal(original.Id, replay.Id);
                Assert.Equal(original.BusinessId, replay.BusinessId);
                Assert.Equal(original.LocationId, replay.LocationId);
                Assert.Equal(original.CustomerId, replay.CustomerId);
                Assert.Equal(original.ProfessionalId, replay.ProfessionalId);
                Assert.Equal(original.StartsAt, replay.StartsAt);
                Assert.Equal(original.EndsAt, replay.EndsAt);
                Assert.Equal(original.Status, replay.Status);
                Assert.Equal(original.TotalPrice, replay.TotalPrice);
                Assert.Equal(
                    original.TotalDurationMinutes,
                    replay.TotalDurationMinutes);
                Assert.Equal(
                    original.ReservationExpiresAt,
                    replay.ReservationExpiresAt);
                Assert.Equal(original.CreatedAt, replay.CreatedAt);
                Assert.Equal(original.UpdatedAt, replay.UpdatedAt);

                Assert.Equal(
                    original.Items.ToArray(),
                    replay.Items.ToArray());
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync();

            // Replay não reativa a reserva expirada.
            Assert.Equal(original.Id, persisted.Id);
            Assert.Equal(AppointmentStatus.Expired, persisted.Status);
            Assert.Equal(expiredAt, persisted.UpdatedAt);

            // Os snapshots continuam independentes do catálogo.
            Assert.Equal(65m, persisted.TotalPriceSnapshot);
            Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);

            var item = Assert.Single(persisted.Items);

            Assert.Equal("Corte", item.ServiceNameSnapshot);
            Assert.Equal(65m, item.PriceSnapshot);
            Assert.Equal(45, item.DurationMinutesSnapshot);

            Assert.Equal(
                1,
                await readContext.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_ShouldPersistReservationAndIdempotency()
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3)),
            "appointment-create-test");

        try
        {
            await SeedBookableAsync(data);

            AppointmentResponse response;

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = CreateHandler(context);

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);
                Assert.Equal(CreateAppointmentError.None, result.Error);
                Assert.NotNull(result.Response);

                response = result.Response;

                Assert.NotEqual(Guid.Empty, response.Id);
                Assert.Equal(data.BusinessId, response.BusinessId);
                Assert.Equal(data.LocationId, response.LocationId);
                Assert.Equal(data.CustomerId, response.CustomerId);
                Assert.Equal(data.ProfessionalId, response.ProfessionalId);
                Assert.Equal("PENDING", response.Status);
                Assert.Equal(65m, response.TotalPrice);
                Assert.Equal(45, response.TotalDurationMinutes);

                Assert.Equal(
                    command.StartsAt.ToUniversalTime(),
                    response.StartsAt);

                Assert.Equal(
                    response.StartsAt.AddMinutes(45),
                    response.EndsAt);

                Assert.Equal(TimeSpan.Zero, response.StartsAt.Offset);

                Assert.Equal<DateTimeOffset?>(
                    CreatedAt.AddMinutes(10),
                    response.ReservationExpiresAt);

                Assert.Equal(CreatedAt, response.CreatedAt);
                Assert.Equal(CreatedAt, response.UpdatedAt);

                var item = Assert.Single(response.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.Name);
                Assert.Equal(65m, item.Price);
                Assert.Equal(45, item.DurationMinutes);
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync();

            Assert.Equal(response.Id, persisted.Id);
            Assert.Equal(response.StartsAt, persisted.StartsAt);
            Assert.Equal(response.EndsAt, persisted.EndsAt);
            Assert.Equal(response.TotalPrice, persisted.TotalPriceSnapshot);
            Assert.Equal(
                response.TotalDurationMinutes,
                persisted.TotalDurationMinutesSnapshot);

            Assert.Equal(
                data.ServiceId,
                Assert.Single(persisted.Items).ServiceId);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(data.TenantId, record.TenantId);
            Assert.Equal("CreateAppointment", record.Operation);
            Assert.Equal(command.IdempotencyKey, record.Key);

            Assert.Equal(
                AppointmentRequestHasher.Compute(command),
                record.RequestHash);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<AppointmentResponse>(
                        record.ResponseJson);

            Assert.NotNull(storedResponse);
            Assert.Equal(response.Id, storedResponse.Id);
            Assert.Equal(response.StartsAt, storedResponse.StartsAt);
            Assert.Equal(response.TotalPrice, storedResponse.TotalPrice);
            Assert.Equal(
                response.Items.ToArray(),
                storedResponse.Items.ToArray());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_WithConcurrentOverlap_ShouldPersistOnlyOne()
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            var gate = new ConcurrentSaveGate();

            await using var firstContext =
                CreateContext(data.TenantId, gate);

            await using var secondContext =
                CreateContext(data.TenantId, gate);

            firstContext.Appointments.Add(
                CreateAppointment(data, firstId, startsAt));

            secondContext.Appointments.Add(
                CreateAppointment(data, secondId, startsAt));

            async Task<Exception?> TrySaveAsync(AppDbContext context)
            {
                try
                {
                    await context.SaveChangesAsync();
                    return null;
                }
                catch (DbUpdateException exception)
                {
                    return exception;
                }
            }

            var results = await Task.WhenAll(
                TrySaveAsync(firstContext),
                TrySaveAsync(secondContext));

            Assert.Equal(2, gate.Arrivals);
            Assert.Single(results, result => result is null);

            var failure = Assert.Single(
                results,
                result => result is not null);

            var updateException = Assert.IsType<DbUpdateException>(failure);

            var postgresException =
                Assert.IsType<Npgsql.PostgresException>(
                    updateException.InnerException);

            Assert.Equal(
                Npgsql.PostgresErrorCodes.ExclusionViolation,
                postgresException.SqlState);

            Assert.Equal(
                "ex_appointments_no_overlap",
                postgresException.ConstraintName);

            var winnerId = results[0] is null ? firstId : secondId;
            var loserId = results[0] is null ? secondId : firstId;

            await using var readContext = CreateContext(data.TenantId);

            var appointment = Assert.Single(
                await readContext.Appointments
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(winnerId, appointment.Id);

            var item = Assert.Single(
                await readContext.AppointmentItems
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(winnerId, item.AppointmentId);

            Assert.False(
                await readContext.Appointments
                    .AnyAsync(entity => entity.Id == loserId));

            Assert.False(
                await readContext.AppointmentItems
                    .AnyAsync(entity => entity.AppointmentId == loserId));
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentAndItems_ShouldBeInvisibleToAnotherTenant()
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentAId = Guid.NewGuid();
        var appointmentBId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataA.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(dataA, appointmentAId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(dataB, appointmentBId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var contextA = CreateContext(dataA.TenantId))
            {
                var appointment = Assert.Single(
                    await contextA.Appointments
                        .AsNoTracking()
                        .Include(entity => entity.Items)
                        .ToListAsync());

                Assert.Equal(appointmentAId, appointment.Id);

                var item = Assert.Single(appointment.Items);
                Assert.Equal(dataA.TenantId, item.TenantId);
                Assert.Equal(appointmentAId, item.AppointmentId);

                Assert.False(
                    await contextA.Appointments
                        .AnyAsync(entity => entity.Id == appointmentBId));

                Assert.False(
                    await contextA.AppointmentItems
                        .AnyAsync(entity =>
                            entity.AppointmentId == appointmentBId));

                var directItem = Assert.Single(
                    await contextA.AppointmentItems
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(appointmentAId, directItem.AppointmentId);

                // Confirma que os dois agregados existem fisicamente.
                Assert.Equal(
                    2,
                    await contextA.Appointments
                        .IgnoreQueryFilters()
                        .CountAsync(entity =>
                            entity.Id == appointmentAId ||
                            entity.Id == appointmentBId));

                Assert.Equal(
                    2,
                    await contextA.AppointmentItems
                        .IgnoreQueryFilters()
                        .CountAsync(entity =>
                            entity.AppointmentId == appointmentAId ||
                            entity.AppointmentId == appointmentBId));
            }

            await using (var contextB = CreateContext(dataB.TenantId))
            {
                var appointment = Assert.Single(
                    await contextB.Appointments
                        .AsNoTracking()
                        .Include(entity => entity.Items)
                        .ToListAsync());

                Assert.Equal(appointmentBId, appointment.Id);
                Assert.Equal(
                    dataB.TenantId,
                    Assert.Single(appointment.Items).TenantId);

                Assert.False(
                    await contextB.Appointments
                        .AnyAsync(entity => entity.Id == appointmentAId));

                Assert.False(
                    await contextB.AppointmentItems
                        .AnyAsync(entity =>
                            entity.AppointmentId == appointmentAId));

                Assert.Equal(
                    appointmentBId,
                    Assert.Single(
                        await contextB.AppointmentItems
                            .AsNoTracking()
                            .ToListAsync()).AppointmentId);
            }
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_WhenExpired_ShouldReleaseInterval()
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);
        var expiredAt = CreatedAt.AddMinutes(10);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, firstId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == firstId);

                Assert.True(appointment.Expire(expiredAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, secondId, startsAt));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(data.TenantId);

            var appointments = await readContext.Appointments
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(2, appointments.Count);

            var expired = Assert.Single(
                appointments,
                entity => entity.Id == firstId);

            Assert.Equal(AppointmentStatus.Expired, expired.Status);
            Assert.False(expired.OccupiesSchedule);
            Assert.Equal(expiredAt, expired.UpdatedAt);
            Assert.Equal(CreatedAt, expired.CreatedAt);

            var replacement = Assert.Single(
                appointments,
                entity => entity.Id == secondId);

            Assert.Equal(AppointmentStatus.Pending, replacement.Status);
            Assert.True(replacement.OccupiesSchedule);
            Assert.Equal(startsAt, replacement.StartsAt);

            Assert.Equal(
                2,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_WithOverlapInAnotherLocation_ShouldReject()
    {
        var data = new TestData();
        var otherLocationId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Locations.Add(
                    new Location(
                        otherLocationId,
                        data.TenantId,
                        data.BusinessId,
                        "Other Location",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        data.TenantId,
                        data.ProfessionalId,
                        otherLocationId,
                        CreatedAt));

                context.Appointments.Add(
                    CreateAppointment(data, firstId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    new Appointment(
                        secondId,
                        data.TenantId,
                        data.BusinessId,
                        otherLocationId,
                        data.CustomerId,
                        data.ProfessionalId,
                        startsAt,
                        CreatedAt,
                        TimeSpan.FromMinutes(10),
                        [
                            new AppointmentItem(
                                Guid.NewGuid(),
                                data.TenantId,
                                secondId,
                                data.ServiceId,
                                "Corte",
                                65m,
                                45)
                        ]));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ExclusionViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "ex_appointments_no_overlap",
                    postgresException.ConstraintName);
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            var persisted = Assert.Single(
                await verificationContext.Appointments
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(firstId, persisted.Id);
            Assert.Equal(data.LocationId, persisted.LocationId);

            Assert.False(
                await verificationContext.AppointmentItems
                    .AnyAsync(item => item.AppointmentId == secondId));
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_ShouldPersistPendingReservationAndSnapshots()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    new Appointment(
                        appointmentId,
                        data.TenantId,
                        data.BusinessId,
                        data.LocationId,
                        data.CustomerId,
                        data.ProfessionalId,
                        startsAt.ToOffset(TimeSpan.FromHours(-3)),
                        CreatedAt.ToOffset(TimeSpan.FromHours(-3)),
                        TimeSpan.FromMinutes(10),
                        [
                            new AppointmentItem(
                                itemId,
                                data.TenantId,
                                appointmentId,
                                data.ServiceId,
                                "Corte",
                                65m,
                                45)
                        ]));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(data.TenantId);

            var appointment = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(data.TenantId, appointment.TenantId);
            Assert.Equal(data.BusinessId, appointment.BusinessId);
            Assert.Equal(data.LocationId, appointment.LocationId);
            Assert.Equal(data.CustomerId, appointment.CustomerId);
            Assert.Equal(data.ProfessionalId, appointment.ProfessionalId);

            Assert.Equal(AppointmentStatus.Pending, appointment.Status);
            Assert.True(appointment.OccupiesSchedule);
            Assert.Equal(startsAt, appointment.StartsAt);
            Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
            Assert.Equal(65m, appointment.TotalPriceSnapshot);
            Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);

            Assert.Equal<DateTimeOffset?>(
                CreatedAt.AddMinutes(10),
                appointment.ReservationExpiresAt);

            Assert.Equal(CreatedAt, appointment.CreatedAt);
            Assert.Equal(CreatedAt, appointment.UpdatedAt);
            Assert.Equal(TimeSpan.Zero, appointment.StartsAt.Offset);
            Assert.Equal(TimeSpan.Zero, appointment.EndsAt.Offset);

            var item = Assert.Single(appointment.Items);

            Assert.Equal(itemId, item.Id);
            Assert.Equal(data.TenantId, item.TenantId);
            Assert.Equal(appointmentId, item.AppointmentId);
            Assert.Equal(data.ServiceId, item.ServiceId);
            Assert.Equal("Corte", item.ServiceNameSnapshot);
            Assert.Equal(65m, item.PriceSnapshot);
            Assert.Equal(45, item.DurationMinutesSnapshot);

            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    #endregion

    #region Theories

        [Theory]
    [InlineData("location-block")]
    [InlineData("professional-block")]
    [InlineData("outside-working-hours")]
    [InlineData("outside-start-grid")]
    public async Task AppointmentReschedulingService_WithUnavailableDestination_ShouldPreserveOriginalBooking(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var requestedStart = scenario switch
        {
            "outside-working-hours" => originalStart.AddHours(-1),
            "outside-start-grid" => originalStart.AddMinutes(7),
            _ => originalStart.AddHours(1)
        };

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();

                if (scenario is "location-block" or "professional-block")
                {
                    context.ScheduleBlocks.Add(
                        new ScheduleBlock(
                            Guid.NewGuid(),
                            data.TenantId,
                            data.LocationId,
                            scenario == "professional-block"
                                ? data.ProfessionalId
                                : null,
                            requestedStart,
                            requestedStart.AddMinutes(30),
                            CreatedAt));

                    await context.SaveChangesAsync();
                }
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                requestedStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(
                    RescheduleAppointmentError.SlotUnavailable,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(originalStart, persisted.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    persisted.EndsAt);
                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
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
    [InlineData(
        "missing-location",
        RescheduleAppointmentError.LocationNotFound)]
    [InlineData(
        "missing-professional",
        RescheduleAppointmentError.ProfessionalNotFound)]
    [InlineData(
        "inactive-location",
        RescheduleAppointmentError.InactiveLocation)]
    [InlineData(
        "inactive-professional",
        RescheduleAppointmentError.InactiveProfessional)]
    [InlineData(
        "service-not-offered",
        RescheduleAppointmentError.ServicesNotOffered)]
    [InlineData(
        "professional-not-eligible",
        RescheduleAppointmentError.ProfessionalNotEligible)]
    public async Task AppointmentReschedulingService_WithInvalidResources_ShouldPreserveOriginalBooking(
        string scenario,
        RescheduleAppointmentError expectedError)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();

                switch (scenario)
                {
                    case "inactive-location":
                    {
                        var location = await context.Locations
                            .SingleAsync(entity => entity.Id == data.LocationId);

                        location.Update(
                            "Unidade inativa",
                            "BR",
                            "America/Sao_Paulo",
                            false,
                            now);

                        break;
                    }

                    case "inactive-professional":
                    {
                        var professional = await context.Professionals
                            .SingleAsync(entity =>
                                entity.Id == data.ProfessionalId);

                        professional.Update(
                            "Profissional inativo",
                            false,
                            now);

                        break;
                    }

                    case "service-not-offered":
                    {
                        var link = await context.LocationServices
                            .SingleAsync(entity =>
                                entity.LocationId == data.LocationId &&
                                entity.ServiceId == data.ServiceId);

                        context.LocationServices.Remove(link);
                        break;
                    }

                    case "professional-not-eligible":
                    {
                        var link = await context.ProfessionalServices
                            .SingleAsync(entity =>
                                entity.ProfessionalId == data.ProfessionalId &&
                                entity.ServiceId == data.ServiceId);

                        link.Update(false, now);
                        break;
                    }
                }

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                scenario == "missing-location"
                    ? Guid.NewGuid()
                    : data.LocationId,
                scenario == "missing-professional"
                    ? Guid.NewGuid()
                    : data.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(expectedError, result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(originalStart, persisted.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    persisted.EndsAt);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
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
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    public async Task AppointmentReschedulingService_ShouldRespectReservationDeadline(
        int elapsedSeconds,
        bool shouldExpire)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddHours(1);
        var now = CreatedAt.AddSeconds(elapsedSeconds);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(!shouldExpire, result.IsSuccess);

                if (shouldExpire)
                {
                    Assert.Equal(
                        RescheduleAppointmentError.ReservationExpired,
                        result.Error);
                    Assert.Null(result.Response);
                }
                else
                {
                    var response = Assert.IsType<AppointmentResponse>(
                        result.Response);

                    Assert.Equal(newStart, response.StartsAt);
                    Assert.Equal(
                        CreatedAt.AddMinutes(10),
                        response.ReservationExpiresAt);
                }
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                var expectedStart = shouldExpire
                    ? originalStart
                    : newStart;

                Assert.Equal(expectedStart, persisted.StartsAt);
                Assert.Equal(
                    expectedStart.AddMinutes(45),
                    persisted.EndsAt);

                Assert.Equal(
                    shouldExpire
                        ? AppointmentStatus.Expired
                        : AppointmentStatus.Pending,
                    persisted.Status);

                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
                Assert.Single(persisted.Items);
                Assert.Null(persisted.CancelledAt);
                Assert.Null(persisted.CancellationReason);

                Assert.Equal(
                    shouldExpire ? 0 : 1,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("appointment")]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("start")]
    public async Task AppointmentReschedulingService_WithSameKeyAndDifferentContent_ShouldConflict(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddHours(1);
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                Assert.True(
                    (await handler.HandleAsync(command)).IsSuccess);
            }

            var conflictingCommand = scenario switch
            {
                "appointment" => command with
                {
                    AppointmentId = Guid.NewGuid()
                },
                "location" => command with
                {
                    LocationId = Guid.NewGuid()
                },
                "professional" => command with
                {
                    ProfessionalId = Guid.NewGuid()
                },
                "start" => command with
                {
                    StartsAt = newStart.AddMinutes(15)
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now.AddMinutes(1))));

                var result = await handler.HandleAsync(
                    conflictingCommand);

                Assert.Equal(
                    RescheduleAppointmentError.IdempotencyConflict,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(newStart, persisted.StartsAt);
                Assert.Equal(newStart.AddMinutes(45), persisted.EndsAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);

                Assert.Equal(
                    1,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    1,
                    await context.Appointments.CountAsync());
                Assert.Equal(
                    1,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    public async Task AppointmentCancellationService_ShouldRespectReservationDeadline(
        int elapsedSeconds,
        bool shouldExpire)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddSeconds(elapsedSeconds);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(!shouldExpire, result.IsSuccess);

                if (shouldExpire)
                {
                    Assert.Equal(
                        CancelAppointmentError.ReservationExpired,
                        result.Error);
                    Assert.Null(result.Response);
                }
                else
                {
                    Assert.NotNull(result.Response);
                    Assert.Equal(now, result.Response.CancelledAt);
                }
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    shouldExpire
                        ? AppointmentStatus.Expired
                        : AppointmentStatus.CancelledByClient,
                    appointment.Status);

                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(now, appointment.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    appointment.ReservationExpiresAt);

                if (shouldExpire)
                {
                    Assert.Null(appointment.CancellationReason);
                    Assert.Null(appointment.CancelledAt);
                }
                else
                {
                    Assert.Equal(
                        "Cliente solicitou",
                        appointment.CancellationReason);
                    Assert.Equal(now, appointment.CancelledAt);
                }

                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);

                Assert.Equal(
                    shouldExpire ? 0 : 1,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("origin")]
    [InlineData("appointment")]
    public async Task AppointmentCancellationService_WithSameKeyAndDifferentContent_ShouldConflict(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var otherAppointmentId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var cancelledAt = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        otherAppointmentId,
                        CreatedAt.AddHours(3)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Motivo original",
                key);

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(cancelledAt)));

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);
            }

            var conflictingCommand = scenario switch
            {
                "reason" => command with
                {
                    Reason = "Outro motivo"
                },
                "origin" => command with
                {
                    CancellationStatus =
                        AppointmentStatus.CancelledByBusiness
                },
                "appointment" => command with
                {
                    AppointmentId = otherAppointmentId
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(CreatedAt.AddMinutes(6))));

                var result = await handler.HandleAsync(
                    conflictingCommand);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    CancelAppointmentError.IdempotencyConflict,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointments = await context.Appointments
                    .AsNoTracking()
                    .ToListAsync();

                var cancelled = Assert.Single(
                    appointments,
                    entity => entity.Id == appointmentId);

                Assert.Equal(
                    AppointmentStatus.CancelledByClient,
                    cancelled.Status);
                Assert.Equal(
                    "Motivo original",
                    cancelled.CancellationReason);
                Assert.Equal(cancelledAt, cancelled.CancelledAt);
                Assert.Equal(cancelledAt, cancelled.UpdatedAt);

                var untouched = Assert.Single(
                    appointments,
                    entity => entity.Id == otherAppointmentId);

                Assert.Equal(
                    AppointmentStatus.Pending,
                    untouched.Status);
                Assert.Null(untouched.CancellationReason);
                Assert.Null(untouched.CancelledAt);
                Assert.Equal(CreatedAt, untouched.UpdatedAt);

                Assert.Equal(
                    1,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    2,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("missing-reason")]
    [InlineData("blank-reason")]
    [InlineData("missing-date")]
    [InlineData("date-before-creation")]
    [InlineData("date-after-update")]
    [InlineData("reason-without-cancellation")]
    [InlineData("date-without-cancellation")]
    public async Task AppointmentCancellation_WithInvalidData_ShouldReject(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var status = "CANCELLED_BY_CLIENT";
            string? reason = "Motivo válido";
            DateTimeOffset? cancelledAt = CreatedAt.AddMinutes(5);
            var updatedAt = CreatedAt.AddMinutes(5);

            switch (scenario)
            {
                case "missing-reason":
                    reason = null;
                    break;

                case "blank-reason":
                    reason = "   ";
                    break;

                case "missing-date":
                    cancelledAt = null;
                    break;

                case "date-before-creation":
                    cancelledAt = CreatedAt.AddMinutes(-1);
                    break;

                case "date-after-update":
                    cancelledAt = updatedAt.AddMinutes(1);
                    break;

                case "reason-without-cancellation":
                    status = "PENDING";
                    cancelledAt = null;
                    break;

                case "date-without-cancellation":
                    status = "PENDING";
                    reason = null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            await using (var context = CreateContext(data.TenantId))
            {
                // SQL direto exercita o CHECK sem passar pelo domínio.
                var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(
                    async () =>
                    {
                        await context.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            UPDATE appointments
                            SET status = {status},
                                cancellation_reason = {reason},
                                cancelled_at = {cancelledAt},
                                updated_at = {updatedAt}
                            WHERE tenant_id = {data.TenantId}
                              AND id = {appointmentId}
                            """);
                    });

                Assert.Equal("23514", exception.SqlState);
                Assert.Equal(
                    "CK_appointments_cancellation",
                    exception.ConstraintName);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Null(persisted.CancellationReason);
                Assert.Null(persisted.CancelledAt);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledByBusiness)]
    public async Task AppointmentCancellation_ShouldPersistAuditAndReleaseInterval(
        AppointmentStatus cancellationStatus)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var replacementId = Guid.NewGuid();

        var startsAt = CreatedAt.AddHours(2);
        var cancelledAt = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.True(appointment.Cancel(
                    cancellationStatus,
                    "  Cancelamento solicitado  ",
                    cancelledAt));

                await context.SaveChangesAsync();
            }

            // Outro contexto confirma os valores efetivamente persistidos.
            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(cancellationStatus, appointment.Status);
                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(
                    "Cancelamento solicitado",
                    appointment.CancellationReason);
                Assert.Equal(cancelledAt, appointment.CancelledAt);
                Assert.Equal(cancelledAt, appointment.UpdatedAt);
                Assert.Equal(CreatedAt, appointment.CreatedAt);

                Assert.Equal(startsAt, appointment.StartsAt);
                Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    appointment.ReservationExpiresAt);

                var item = Assert.Single(appointment.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.ServiceNameSnapshot);
                Assert.Equal(65m, item.PriceSnapshot);
                Assert.Equal(45, item.DurationMinutesSnapshot);
            }

            // A exclusion constraint deve permitir ocupar o horário liberado.
            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        replacementId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointments = await context.Appointments
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, appointments.Count);

                var replacement = Assert.Single(
                    appointments,
                    entity => entity.Id == replacementId);

                Assert.Equal(
                    AppointmentStatus.Pending,
                    replacement.Status);
                Assert.True(replacement.OccupiesSchedule);
                Assert.Null(replacement.CancellationReason);
                Assert.Null(replacement.CancelledAt);

                Assert.Equal(
                    2,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("missing-customer", CreateAppointmentError.CustomerNotFound)]
    [InlineData("missing-location", CreateAppointmentError.LocationNotFound)]
    [InlineData("missing-professional", CreateAppointmentError.ProfessionalNotFound)]
    [InlineData("missing-service", CreateAppointmentError.ServiceNotFound)]
    [InlineData("inactive-service", CreateAppointmentError.InvalidServices)]
    [InlineData("not-offered", CreateAppointmentError.ServicesNotOffered)]
    [InlineData("not-eligible", CreateAppointmentError.ProfessionalNotEligible)]
    public async Task AppointmentCreation_WithUnavailableResource_ShouldNotPersist(
        string scenario,
        CreateAppointmentError expectedError)
    {
        var data = new TestData();

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                if (scenario == "inactive-service")
                {
                    var service = await context.Services
                        .SingleAsync(entity => entity.Id == data.ServiceId);

                    service.Update(
                        service.Name,
                        service.Price,
                        service.DurationMinutes,
                        false,
                        CreatedAt.AddMinutes(1));
                }
                else if (scenario == "not-offered")
                {
                    var link = await context.LocationServices
                        .SingleAsync(entity =>
                            entity.LocationId == data.LocationId &&
                            entity.ServiceId == data.ServiceId);

                    context.LocationServices.Remove(link);
                }
                else if (scenario == "not-eligible")
                {
                    var link = await context.ProfessionalServices
                        .SingleAsync(entity =>
                            entity.ProfessionalId == data.ProfessionalId &&
                            entity.ServiceId == data.ServiceId);

                    context.ProfessionalServices.Remove(link);
                }

                await context.SaveChangesAsync();
            }

            var command = new CreateAppointmentCommand(
                scenario == "missing-customer"
                    ? Guid.NewGuid()
                    : data.CustomerId,
                scenario == "missing-location"
                    ? Guid.NewGuid()
                    : data.LocationId,
                scenario == "missing-professional"
                    ? Guid.NewGuid()
                    : data.ProfessionalId,
                [
                    scenario == "missing-service"
                        ? Guid.NewGuid()
                        : data.ServiceId
                ],
                new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
                $"unavailable-resource-{scenario}");

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(command);

                Assert.False(result.IsSuccess);
                Assert.Equal(expectedError, result.Error);
                Assert.Null(result.Response);
            }

            await using var readContext = CreateContext(data.TenantId);

            Assert.False(await readContext.Appointments.AnyAsync());
            Assert.False(await readContext.AppointmentItems.AnyAsync());
            Assert.False(await readContext.IdempotencyRecords.AnyAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("outside-window")]
    [InlineData("off-grid")]
    [InlineData("wrong-day")]
    [InlineData("past")]
    [InlineData("location-block")]
    [InlineData("professional-block")]
    [InlineData("occupied")]
    public async Task AppointmentCreation_WithUnavailableSlot_ShouldNotPersist(
        string scenario)
    {
        var data = new TestData();

        // Segunda-feira, 9h em São Paulo.
        var startsAt = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var requestedStart = scenario switch
        {
            "outside-window" => startsAt.AddHours(-1),
            "off-grid" => startsAt.AddMinutes(1),
            "wrong-day" => startsAt.AddDays(1),
            _ => startsAt
        };

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            requestedStart,
            $"unavailable-slot-{scenario}");

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                if (scenario is "location-block" or "professional-block")
                {
                    context.ScheduleBlocks.Add(
                        new ScheduleBlock(
                            Guid.NewGuid(),
                            data.TenantId,
                            data.LocationId,
                            scenario == "professional-block"
                                ? data.ProfessionalId
                                : (Guid?)null,
                            startsAt.AddMinutes(10),
                            startsAt.AddMinutes(20),
                            CreatedAt));
                }

                if (scenario == "occupied")
                {
                    context.Appointments.Add(
                        CreateAppointment(
                            data,
                            Guid.NewGuid(),
                            startsAt));
                }

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var now = scenario == "past"
                    ? startsAt.AddMinutes(1)
                    : CreatedAt;

                var result = await CreateHandler(context, now)
                    .HandleAsync(command);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    CreateAppointmentError.SlotUnavailable,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using var readContext = CreateContext(data.TenantId);

            var expectedCount = scenario == "occupied" ? 1 : 0;

            Assert.Equal(
                expectedCount,
                await readContext.Appointments.CountAsync());

            Assert.Equal(
                expectedCount,
                await readContext.AppointmentItems.CountAsync());

            Assert.False(
                await readContext.IdempotencyRecords.AnyAsync());
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
    [InlineData("start")]
    public async Task AppointmentCreation_WithSameKeyAndDifferentContent_ShouldConflict(
        string changedField)
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            "appointment-content-conflict");

        try
        {
            await SeedBookableAsync(data);

            AppointmentResponse original;

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(command);

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);

                original = result.Response;
            }

            var changedCommand = changedField switch
            {
                "customer" => command with
                {
                    CustomerId = Guid.NewGuid()
                },
                "location" => command with
                {
                    LocationId = Guid.NewGuid()
                },
                "professional" => command with
                {
                    ProfessionalId = Guid.NewGuid()
                },
                "service" => command with
                {
                    ServiceIds = [Guid.NewGuid()]
                },
                "start" => command with
                {
                    StartsAt = command.StartsAt.AddMinutes(15)
                },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(changedField))
            };

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(changedCommand);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    CreateAppointmentError.IdempotencyConflict,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(original.Id, persisted.Id);
            Assert.Equal(original.StartsAt, persisted.StartsAt);
            Assert.Equal(AppointmentStatus.Pending, persisted.Status);
            Assert.Equal(CreatedAt, persisted.UpdatedAt);

            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(command.IdempotencyKey, record.Key);
            Assert.Equal(
                AppointmentRequestHasher.Compute(command),
                record.RequestHash);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending, true)]
    [InlineData(AppointmentStatus.Confirmed, true)]
    [InlineData(AppointmentStatus.ConfirmedByClient, true)]
    [InlineData(AppointmentStatus.RescheduleRequested, true)]
    [InlineData(AppointmentStatus.CancelledByClient, false)]
    [InlineData(AppointmentStatus.CancelledByBusiness, false)]
    [InlineData(AppointmentStatus.Completed, false)]
    [InlineData(AppointmentStatus.NoShow, false)]
    [InlineData(AppointmentStatus.Expired, false)]
    public async Task Appointment_ShouldApplyOverlapProtectionByStatus(
        AppointmentStatus status,
        bool shouldConflict)
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                var first = CreateAppointment(data, firstId, startsAt);

                context.Appointments.Add(first);

                if (status is AppointmentStatus.CancelledByClient
                    or AppointmentStatus.CancelledByBusiness)
                {
                    first.Cancel(
                        status,
                        "Cancelamento para teste de sobreposição",
                        CreatedAt.AddMinutes(5));
                }
                else
                {
                    // Prepara os demais estados para testar a constraint.
                    context.Entry(first)
                        .Property(entity => entity.Status)
                        .CurrentValue = status;
                }

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, secondId, startsAt));

                if (shouldConflict)
                {
                    var exception =
                        await Assert.ThrowsAsync<DbUpdateException>(
                            () => context.SaveChangesAsync());

                    var postgresException =
                        Assert.IsType<Npgsql.PostgresException>(
                            exception.InnerException);

                    Assert.Equal(
                        Npgsql.PostgresErrorCodes.ExclusionViolation,
                        postgresException.SqlState);

                    Assert.Equal(
                        "ex_appointments_no_overlap",
                        postgresException.ConstraintName);
                }
                else
                {
                    await context.SaveChangesAsync();
                }
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == firstId);

            Assert.Equal(status, persisted.Status);
            Assert.Equal(shouldConflict, persisted.OccupiesSchedule);

            Assert.Equal(
                !shouldConflict,
                await readContext.Appointments
                    .AnyAsync(entity => entity.Id == secondId));

            Assert.Equal(
                shouldConflict ? 1 : 2,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(
        "business",
        "FK_appointments_businesses_tenant_id_business_id")]
    [InlineData(
        "customer",
        "FK_appointments_customers_tenant_id_customer_id")]
    [InlineData(
        "professional-location",
        "FK_appointments_professional_locations_tenant_id_professional_~")]
    public async Task Appointment_WithCrossTenantReference_ShouldReject(
        string reference,
        string expectedConstraint)
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentId = Guid.NewGuid();

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    new Appointment(
                        appointmentId,
                        dataB.TenantId,
                        reference == "business"
                            ? dataA.BusinessId
                            : dataB.BusinessId,
                        reference == "professional-location"
                            ? dataA.LocationId
                            : dataB.LocationId,
                        reference == "customer"
                            ? dataA.CustomerId
                            : dataB.CustomerId,
                        reference == "professional-location"
                            ? dataA.ProfessionalId
                            : dataB.ProfessionalId,
                        CreatedAt.AddHours(2),
                        CreatedAt,
                        TimeSpan.FromMinutes(10),
                        [
                            new AppointmentItem(
                                Guid.NewGuid(),
                                dataB.TenantId,
                                appointmentId,
                                dataB.ServiceId,
                                "Corte",
                                65m,
                                45)
                        ]));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    expectedConstraint,
                    postgresException.ConstraintName);
            }

            await using var verificationContext =
                CreateContext(dataB.TenantId);

            Assert.False(
                await verificationContext.Appointments
                    .IgnoreQueryFilters()
                    .AnyAsync(entity => entity.Id == appointmentId));

            Assert.False(
                await verificationContext.AppointmentItems
                    .IgnoreQueryFilters()
                    .AnyAsync(item =>
                        item.AppointmentId == appointmentId));
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Theory]
    [InlineData(
        "appointment",
        "FK_appointment_items_appointments_tenant_id_appointment_id")]
    [InlineData(
        "service",
        "FK_appointment_items_services_tenant_id_service_id")]
    public async Task AppointmentItem_WithCrossTenantReference_ShouldReject(
        string reference,
        string expectedConstraint)
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentAId = Guid.NewGuid();
        var appointmentBId = Guid.NewGuid();
        var attemptedItemId = Guid.NewGuid();

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataA.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataA,
                        appointmentAId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataB,
                        appointmentBId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.AppointmentItems.Add(
                    new AppointmentItem(
                        attemptedItemId,
                        dataB.TenantId,
                        reference == "appointment"
                            ? appointmentAId
                            : appointmentBId,
                        reference == "service"
                            ? dataA.ServiceId
                            : dataB.ServiceId,
                        "Invalid Reference",
                        10m,
                        10));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    expectedConstraint,
                    postgresException.ConstraintName);
            }

            await using var verificationContext =
                CreateContext(dataB.TenantId);

            Assert.False(
                await verificationContext.AppointmentItems
                    .IgnoreQueryFilters()
                    .AnyAsync(item => item.Id == attemptedItemId));

            Assert.Equal(
                2,
                await verificationContext.AppointmentItems
                    .IgnoreQueryFilters()
                    .CountAsync(item =>
                        item.AppointmentId == appointmentAId ||
                        item.AppointmentId == appointmentBId));
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task AppointmentItem_ShouldRejectCrossTenantWrites(
        string operation)
    {
        var data = new TestData();
        var otherTenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var attemptedItemId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var otherContext = CreateContext(otherTenantId))
            {
                if (operation == "insert")
                {
                    otherContext.AppointmentItems.Add(
                        new AppointmentItem(
                            attemptedItemId,
                            data.TenantId,
                            appointmentId,
                            data.ServiceId,
                            "Attempted Item",
                            1m,
                            1));
                }
                else
                {
                    // Bypass intencional para testar a proteção de escrita.
                    var item = await otherContext.AppointmentItems
                        .IgnoreQueryFilters()
                        .SingleAsync(entity =>
                            entity.AppointmentId == appointmentId);

                    if (operation == "update")
                    {
                        // Simula alteração fora da API pública do domínio.
                        otherContext.Entry(item)
                            .Property(entity => entity.PriceSnapshot)
                            .CurrentValue = 1m;
                    }
                    else
                    {
                        otherContext.AppointmentItems.Remove(item);
                    }
                }

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => otherContext.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            var persisted = await verificationContext.AppointmentItems
                .AsNoTracking()
                .SingleAsync(item => item.AppointmentId == appointmentId);

            Assert.NotEqual(attemptedItemId, persisted.Id);
            Assert.Equal(data.TenantId, persisted.TenantId);
            Assert.Equal(data.ServiceId, persisted.ServiceId);
            Assert.Equal("Corte", persisted.ServiceNameSnapshot);
            Assert.Equal(65m, persisted.PriceSnapshot);
            Assert.Equal(45, persisted.DurationMinutesSnapshot);

            var appointment = await verificationContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(65m, appointment.TotalPriceSnapshot);
            Assert.Equal(AppointmentStatus.Pending, appointment.Status);
            Assert.Equal(CreatedAt, appointment.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Appointment_ShouldRejectCrossTenantWrites(
        string operation)
    {
        var data = new TestData();
        var otherTenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            if (operation != "insert")
            {
                await using var context = CreateContext(data.TenantId);

                context.Appointments.Add(
                    CreateAppointment(data, appointmentId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var otherContext = CreateContext(otherTenantId))
            {
                if (operation == "insert")
                {
                    otherContext.Appointments.Add(
                        CreateAppointment(data, appointmentId, startsAt));
                }
                else
                {
                    // Bypass intencional para testar a proteção de escrita.
                    var appointment = await otherContext.Appointments
                        .IgnoreQueryFilters()
                        .SingleAsync(entity => entity.Id == appointmentId);

                    if (operation == "update")
                    {
                        Assert.True(
                            appointment.Expire(CreatedAt.AddMinutes(10)));
                    }
                    else
                    {
                        otherContext.Appointments.Remove(appointment);
                    }
                }

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => otherContext.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            var persisted = await verificationContext.Appointments
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == appointmentId);

            if (operation == "insert")
            {
                Assert.Null(persisted);

                Assert.False(
                    await verificationContext.AppointmentItems
                        .AnyAsync(item =>
                            item.AppointmentId == appointmentId));
            }
            else
            {
                Assert.NotNull(persisted);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(startsAt, persisted.StartsAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);

                Assert.Equal(
                    1,
                    await verificationContext.AppointmentItems
                        .CountAsync(item =>
                            item.AppointmentId == appointmentId));
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(-45, false)]
    [InlineData(45, false)]
    public async Task Appointment_ShouldRejectOverlapAndAllowAdjacentIntervals(
        int secondStartOffsetMinutes,
        bool shouldConflict)
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstStart = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, firstId, firstStart));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        secondId,
                        firstStart.AddMinutes(secondStartOffsetMinutes)));

                if (shouldConflict)
                {
                    var exception =
                        await Assert.ThrowsAsync<DbUpdateException>(
                            () => context.SaveChangesAsync());

                    var postgresException =
                        Assert.IsType<Npgsql.PostgresException>(
                            exception.InnerException);

                    Assert.Equal(
                        Npgsql.PostgresErrorCodes.ExclusionViolation,
                        postgresException.SqlState);

                    Assert.Equal(
                        "ex_appointments_no_overlap",
                        postgresException.ConstraintName);
                }
                else
                {
                    await context.SaveChangesAsync();
                }
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            Assert.True(
                await verificationContext.Appointments
                    .AnyAsync(entity => entity.Id == firstId));

            Assert.Equal(
                !shouldConflict,
                await verificationContext.Appointments
                    .AnyAsync(entity => entity.Id == secondId));

            Assert.Equal(
                shouldConflict ? 1 : 2,
                await verificationContext.Appointments.CountAsync());

            // A tentativa rejeitada também não deixa seu item persistido.
            Assert.Equal(
                shouldConflict ? 1 : 2,
                await verificationContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    #endregion

    #region Helpers

    private sealed class BeforeCancellationSaveInterceptor(
        Func<Task> beforeSave)
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private bool _executed;

        public override async ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (!_executed)
            {
                _executed = true;
                await beforeSave();
            }

            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static CreateAppointmentHandler CreateHandler(
        AppDbContext context,
        DateTimeOffset? now = null,
        int lifetimeMinutes = 10)
    {
        var service = new AppointmentCreationService(
            context,
            Options.Create(new AppointmentReservationOptions
            {
                LifetimeMinutes = lifetimeMinutes
            }),
            new FixedTimeProvider(now ?? CreatedAt));

        return new CreateAppointmentHandler(service);
    }

    private async Task SeedBookableAsync(TestData data)
    {
        await SeedAsync(data);

        await using var context = CreateContext(data.TenantId);

        context.LocationServices.Add(
            new LocationService(
                data.TenantId,
                data.LocationId,
                data.ServiceId,
                CreatedAt));

        context.ProfessionalServices.Add(
            new ProfessionalService(
                data.TenantId,
                data.ProfessionalId,
                data.ServiceId,
                CreatedAt));

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

    private sealed class ConcurrentSaveGate
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public override async ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _ready.TrySetResult(true);
            }

            await _ready.Task.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);

            return result;
        }
    }
    private AppDbContext CreateContext(
        Guid tenantId,
        Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor?
            interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return new AppDbContext(options.Options, tenantContext);
    }

    private static Appointment CreateAppointment(
        TestData data,
        Guid appointmentId,
        DateTimeOffset startsAt,
        int durationMinutes = 45)
    {
        return new Appointment(
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
                    "Corte",
                    65m,
                    durationMinutes)
            ]);
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

    private async Task SeedAsync(TestData data)
    {
        await using var context = CreateContext(data.TenantId);

        context.Tenants.Add(
            new Tenant(data.TenantId, "Appointment Tenant"));

        context.Businesses.Add(
            new Business(
                data.BusinessId,
                data.TenantId,
                BusinessTypeIds.Barbershop,
                "Appointment Business",
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
