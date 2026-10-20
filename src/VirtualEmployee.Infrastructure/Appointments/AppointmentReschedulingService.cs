using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Application.Appointments.RescheduleAppointment;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Persistence.Idempotency;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentReschedulingService
    : IAppointmentReschedulingService
{
    private const string Operation = "RescheduleAppointment";

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AppointmentReschedulingService(
        AppDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RescheduleAppointmentResult> RescheduleAsync(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var requestHash = ComputeHash(command);

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

            appointment = await _dbContext.Appointments
                .Include(entity => entity.Items)
                .SingleOrDefaultAsync(
                    entity => entity.Id == command.AppointmentId,
                    cancellationToken);

            if (appointment is null)
            {
                return Failure(
                    RescheduleAppointmentError.AppointmentNotFound);
            }

            var now = GetUtcNow();

            if (now < appointment.UpdatedAt)
            {
                return Failure(
                    RescheduleAppointmentError.ConcurrencyConflict);
            }

            if (appointment.Status == AppointmentStatus.Pending &&
                appointment.ReservationExpiresAt is { } expiresAt &&
                now >= expiresAt)
            {
                appointment.Expire(now);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Failure(
                    RescheduleAppointmentError.ReservationExpired);
            }

            if (appointment.Status == AppointmentStatus.Expired)
            {
                return Failure(
                    RescheduleAppointmentError.ReservationExpired);
            }

            if (!appointment.OccupiesSchedule ||
                appointment.StartsAt <= now)
            {
                return Failure(
                    RescheduleAppointmentError.InvalidState);
            }

            var resources =
                await new AppointmentReschedulingResourceValidator(
                        _dbContext)
                    .ValidateAsync(
                        appointment,
                        command,
                        cancellationToken);

            if (resources.Error != RescheduleAppointmentError.None)
            {
                return Failure(resources.Error);
            }

            var availabilityError =
                await new AppointmentAvailabilityValidator(_dbContext)
                    .ValidateAsync(
                        command.LocationId,
                        command.ProfessionalId,
                        resources.ServiceIds,
                        appointment.BusinessId,
                        resources.Timezone!,
                        command.StartsAt,
                        appointment.TotalDurationMinutesSnapshot,
                        now,
                        appointment.Id,
                        cancellationToken);

            if (availabilityError != CreateAppointmentError.None)
            {
                return Failure(availabilityError switch
                {
                    CreateAppointmentError.InvalidTimezone =>
                        RescheduleAppointmentError.InvalidTimezone,

                    CreateAppointmentError.SlotUnavailable =>
                        RescheduleAppointmentError.SlotUnavailable,

                    _ => throw new InvalidOperationException(
                        "Unexpected availability validation result.")
                });
            }

            // As consultas podem consumir parte do prazo da reserva.
            // Reavalia o relógio antes de modificar o agendamento.
            now = GetUtcNow();

            if (now < appointment.UpdatedAt)
            {
                return Failure(
                    RescheduleAppointmentError.ConcurrencyConflict);
            }

            if (appointment.Status == AppointmentStatus.Pending &&
                appointment.ReservationExpiresAt is { } deadline &&
                now >= deadline)
            {
                appointment.Expire(now);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Failure(
                    RescheduleAppointmentError.ReservationExpired);
            }

            if (appointment.StartsAt <= now)
            {
                return Failure(
                    RescheduleAppointmentError.InvalidState);
            }

            if (command.StartsAt <= now)
            {
                return Failure(
                    RescheduleAppointmentError.SlotUnavailable);
            }

            try
            {
                appointment.Reschedule(
                    command.LocationId,
                    command.ProfessionalId,
                    command.StartsAt,
                    now);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Failure(
                    RescheduleAppointmentError.SlotUnavailable);
            }

            // Mesmo destino exige uma escrita para verificar os tokens
            // de concorrência antes de registrar uma nova chave.
            _dbContext.Entry(appointment)
                .Property(entity => entity.StartsAt)
                .IsModified = true;

            var response = CreateResponse(appointment);

            record = new IdempotencyRecord(
                _dbContext.CurrentTenantId,
                Operation,
                command.IdempotencyKey,
                requestHash,
                JsonSerializer.Serialize(response),
                now);

            _dbContext.IdempotencyRecords.Add(record);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return RescheduleAppointmentResult.Success(response);
        }
        catch (DbUpdateConcurrencyException)
        {
            DetachAttempt(appointment, record);

            return Failure(
                RescheduleAppointmentError.ConcurrencyConflict);
        }
        catch (Exception exception)
        {
            DetachAttempt(appointment, record);

            var postgresException = FindPostgresException(exception);

            if (postgresException is
                {
                    SqlState: PostgresErrorCodes.ExclusionViolation,
                    ConstraintName: "ex_appointments_no_overlap"
                })
            {
                return Failure(
                    RescheduleAppointmentError.SlotUnavailable);
            }

            if (postgresException?.SqlState ==
                PostgresErrorCodes.DeadlockDetected)
            {
                return Failure(
                    RescheduleAppointmentError.ConcurrencyConflict);
            }

            throw;
        }
    }

    private static AppointmentResponse CreateResponse(
        Appointment appointment)
    {
        var status = appointment.Status switch
        {
            AppointmentStatus.Pending => "PENDING",
            AppointmentStatus.Confirmed => "CONFIRMED",
            AppointmentStatus.ConfirmedByClient =>
                "CONFIRMED_BY_CLIENT",
            AppointmentStatus.RescheduleRequested =>
                "RESCHEDULE_REQUESTED",
            _ => throw new InvalidOperationException(
                "Appointment must be in a reschedulable state.")
        };

        return new AppointmentResponse(
            appointment.Id,
            appointment.BusinessId,
            appointment.LocationId,
            appointment.CustomerId,
            appointment.ProfessionalId,
            appointment.StartsAt,
            appointment.EndsAt,
            status,
            appointment.TotalPriceSnapshot,
            appointment.TotalDurationMinutesSnapshot,
            appointment.ReservationExpiresAt,
            appointment.Items
                .OrderBy(item => item.ServiceId)
                .Select(item => new AppointmentItemResponse(
                    item.ServiceId,
                    item.ServiceNameSnapshot,
                    item.PriceSnapshot,
                    item.DurationMinutesSnapshot))
                .ToArray(),
            appointment.CreatedAt,
            appointment.UpdatedAt);
    }

    private static string ComputeHash(
        RescheduleAppointmentCommand command)
    {
        var json = JsonSerializer.Serialize(new
        {
            command.AppointmentId,
            command.LocationId,
            command.ProfessionalId,
            StartsAtUtcTicks = command.StartsAt.UtcDateTime.Ticks
        });

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static RescheduleAppointmentResult Replay(
        IdempotencyRecord record,
        string requestHash)
    {
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            return Failure(
                RescheduleAppointmentError.IdempotencyConflict);
        }

        var response = JsonSerializer.Deserialize<AppointmentResponse>(
            record.ResponseJson)
            ?? throw new InvalidOperationException(
                "Stored rescheduling response could not be read.");

        return RescheduleAppointmentResult.Success(response);
    }

    private void DetachAttempt(
        Appointment? appointment,
        IdempotencyRecord? record)
    {
        if (record is not null)
        {
            _dbContext.Entry(record).State = EntityState.Detached;
        }

        if (appointment is not null)
        {
            foreach (var item in appointment.Items.ToArray())
            {
                _dbContext.Entry(item).State = EntityState.Detached;
            }

            _dbContext.Entry(appointment).State = EntityState.Detached;
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        var utc = _timeProvider.GetUtcNow().ToUniversalTime();

        return new DateTimeOffset(
            utc.Ticks - utc.Ticks % 10,
            TimeSpan.Zero);
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

    private static RescheduleAppointmentResult Failure(
        RescheduleAppointmentError error)
    {
        return RescheduleAppointmentResult.Failure(error);
    }
}