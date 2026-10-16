using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Persistence.Idempotency;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentCreationService : IAppointmentCreationService
{
    private const string Operation = "CreateAppointment";
    private const int MaxAttempts = 3;

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly AppointmentReservationOptions _options;

    public AppointmentCreationService(
        AppDbContext dbContext,
        IOptions<AppointmentReservationOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CreateAppointmentResult> CreateAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CreateAttemptAsync(
                    command,
                    cancellationToken);
            }
            catch (Exception exception)
                when (attempt < MaxAttempts &&
                      FindPostgresException(exception)?.SqlState ==
                          PostgresErrorCodes.DeadlockDetected)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(
                        attempt * 50 + Random.Shared.Next(25, 100)),
                    cancellationToken);
            }
        }
    }

    private async Task<CreateAppointmentResult> CreateAttemptAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var requestHash = AppointmentRequestHasher.Compute(command);

        var lockKey =
            $"{_dbContext.CurrentTenantId:D}:{Operation}:" +
            command.IdempotencyKey;

        Appointment? appointment = null;
        IdempotencyRecord? record = null;

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            // Serializa requisições com a mesma chave lógica.
            // O lock é liberado quando a transação termina.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0));",
                cancellationToken);

            var existingRecord = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity =>
                        entity.Operation == Operation &&
                        entity.Key == command.IdempotencyKey,
                    cancellationToken);

            if (existingRecord is not null)
            {
                return Replay(existingRecord, requestHash);
            }

            var resources = await new AppointmentResourceValidator(
                    _dbContext)
                .ValidateAsync(command, cancellationToken);

            if (resources.Error != CreateAppointmentError.None)
            {
                return Failure(resources.Error);
            }

            var availabilityError =
                await new AppointmentAvailabilityValidator(_dbContext)
                    .ValidateAsync(
                        command,
                        resources,
                        GetUtcNow(),
                        cancellationToken);

            if (availabilityError != CreateAppointmentError.None)
            {
                return Failure(availabilityError);
            }

            var now = GetUtcNow();

            if (command.StartsAt <= now)
            {
                return Failure(CreateAppointmentError.SlotUnavailable);
            }

            var quote = resources.Quote
                ?? throw new InvalidOperationException(
                    "Validated resources must contain a quote.");

            var appointmentId = Guid.NewGuid();

            var items = quote.Items
                .Select(item => new AppointmentItem(
                    Guid.NewGuid(),
                    _dbContext.CurrentTenantId,
                    appointmentId,
                    item.ServiceId,
                    item.Name,
                    item.Price,
                    item.DurationMinutes))
                .ToArray();

            try
            {
                appointment = new Appointment(
                    appointmentId,
                    _dbContext.CurrentTenantId,
                    resources.BusinessId,
                    command.LocationId,
                    command.CustomerId,
                    command.ProfessionalId,
                    command.StartsAt,
                    now,
                    TimeSpan.FromMinutes(_options.LifetimeMinutes),
                    items);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Failure(CreateAppointmentError.InvalidTotals);
            }
            catch (OverflowException)
            {
                return Failure(CreateAppointmentError.InvalidTotals);
            }

            var response = new AppointmentResponse(
                appointment.Id,
                appointment.BusinessId,
                appointment.LocationId,
                appointment.CustomerId,
                appointment.ProfessionalId,
                appointment.StartsAt,
                appointment.EndsAt,
                "PENDING",
                appointment.TotalPriceSnapshot,
                appointment.TotalDurationMinutesSnapshot,
                appointment.ReservationExpiresAt,
                items.Select(item => new AppointmentItemResponse(
                    item.ServiceId,
                    item.ServiceNameSnapshot,
                    item.PriceSnapshot,
                    item.DurationMinutesSnapshot))
                    .ToArray(),
                appointment.CreatedAt,
                appointment.UpdatedAt);

            record = new IdempotencyRecord(
                _dbContext.CurrentTenantId,
                Operation,
                command.IdempotencyKey,
                requestHash,
                JsonSerializer.Serialize(response),
                now);

            _dbContext.Appointments.Add(appointment);
            _dbContext.IdempotencyRecords.Add(record);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreateAppointmentResult.Success(response);
        }
        catch (Exception exception)
        {
            // A transação foi descartada antes deste catch.
            // Limpa somente as entidades criadas nesta tentativa.
            DetachAttempt(appointment, record);

            var postgresException = FindPostgresException(exception);

            if (postgresException is
                {
                    SqlState: PostgresErrorCodes.ExclusionViolation,
                    ConstraintName: "ex_appointments_no_overlap"
                })
            {
                return Failure(CreateAppointmentError.SlotUnavailable);
            }

            // Deadlocks são tratados pelo método público, repetindo
            // a operação completa em uma nova transação.
            throw;
        }
    }

        private void DetachAttempt(
        Appointment? appointment,
        IdempotencyRecord? record)
    {
        if (appointment is not null)
        {
            var items = appointment.Items.ToArray();

            foreach (var item in items)
            {
                _dbContext.Entry(item).State = EntityState.Detached;
            }

            _dbContext.Entry(appointment).State = EntityState.Detached;
        }

        if (record is not null)
        {
            _dbContext.Entry(record).State = EntityState.Detached;
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        var utc = _timeProvider.GetUtcNow().ToUniversalTime();

        return new DateTimeOffset(
            utc.Ticks - utc.Ticks % 10,
            TimeSpan.Zero);
    }

    private static CreateAppointmentResult Replay(
        IdempotencyRecord record,
        string requestHash)
    {
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            return Failure(CreateAppointmentError.IdempotencyConflict);
        }

        var response = JsonSerializer.Deserialize<AppointmentResponse>(
            record.ResponseJson)
            ?? throw new InvalidOperationException(
                "Stored appointment response could not be read.");

        return CreateAppointmentResult.Success(response);
    }

    private static PostgresException? FindPostgresException(
        Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }

    private static CreateAppointmentResult Failure(
        CreateAppointmentError error)
    {
        return CreateAppointmentResult.Failure(error);
    }
}