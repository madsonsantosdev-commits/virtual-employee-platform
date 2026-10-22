using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.CancelAppointment;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Persistence.Idempotency;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentCancellationService
    : IAppointmentCancellationService
{
    private const string Operation = "CancelAppointment";

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AppointmentCancellationService(
        AppDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CancelAppointmentResult> CancelAsync(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var requestHash = ComputeHash(command);

        var lockKey =
            $"{_dbContext.CurrentTenantId:D}:{Operation}:" +
            command.IdempotencyKey;

        Appointment? appointment = null;
        IdempotencyRecord? record = null;
        AppointmentHistoryEntry? historyEntry = null;

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
                .SingleOrDefaultAsync(
                    entity => entity.Id == command.AppointmentId,
                    cancellationToken);

            if (appointment is null)
            {
                return Failure(
                    CancelAppointmentError.AppointmentNotFound);
            }

            var now = GetUtcNow();

            if (now < appointment.UpdatedAt)
            {
                return Failure(
                    CancelAppointmentError.ConcurrencyConflict);
            }

            if (appointment.Status == AppointmentStatus.Pending &&
                appointment.ReservationExpiresAt is { } expiresAt &&
                now >= expiresAt)
            {
                var beforeExpiration =
                    AppointmentHistoryState.Capture(appointment);

                appointment.Expire(now);

                historyEntry = new AppointmentHistoryEntry(
                    Guid.NewGuid(),
                    appointment.TenantId,
                    appointment.Id,
                    AppointmentHistoryType.Expired,
                    now,
                    beforeExpiration,
                    AppointmentHistoryState.Capture(appointment));

                _dbContext.AppointmentHistoryEntries.Add(historyEntry);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Failure(
                    CancelAppointmentError.ReservationExpired);
            }

            if (appointment.Status == AppointmentStatus.Expired)
            {
                return Failure(
                    CancelAppointmentError.ReservationExpired);
            }

            if (appointment.Status == command.CancellationStatus)
            {
                // Uma nova chave não pode substituir o motivo original.
                if (!string.Equals(
                        appointment.CancellationReason,
                        command.Reason,
                        StringComparison.Ordinal))
                {
                    return Failure(
                        CancelAppointmentError.InvalidState);
                }
            }
            else if (!appointment.OccupiesSchedule)
            {
                return Failure(
                    CancelAppointmentError.InvalidState);
            }

            var beforeCancellation =
                AppointmentHistoryState.Capture(appointment);

            var changed = appointment.Cancel(
                command.CancellationStatus,
                command.Reason,
                now);

            if (changed)
            {
                historyEntry = new AppointmentHistoryEntry(
                    Guid.NewGuid(),
                    appointment.TenantId,
                    appointment.Id,
                    AppointmentHistoryType.Cancelled,
                    now,
                    beforeCancellation,
                    AppointmentHistoryState.Capture(appointment),
                    appointment.CancellationReason);

                _dbContext.AppointmentHistoryEntries.Add(historyEntry);
            }

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

            return CancelAppointmentResult.Success(response);
        }
        catch (DbUpdateConcurrencyException)
        {
            DetachAttempt(appointment, record, historyEntry);

            return Failure(
                CancelAppointmentError.ConcurrencyConflict);
        }
        catch (Exception exception)
        {
            DetachAttempt(appointment, record, historyEntry);

            if (FindPostgresException(exception)?.SqlState ==
                PostgresErrorCodes.DeadlockDetected)
            {
                return Failure(
                    CancelAppointmentError.ConcurrencyConflict);
            }

            throw;
        }
    }

    private static CancelAppointmentResponse CreateResponse(
        Appointment appointment)
    {
        var status = appointment.Status switch
        {
            AppointmentStatus.CancelledByClient =>
                "CANCELLED_BY_CLIENT",

            AppointmentStatus.CancelledByBusiness =>
                "CANCELLED_BY_BUSINESS",

            _ => throw new InvalidOperationException(
                "Appointment must be cancelled.")
        };

        return new CancelAppointmentResponse(
            appointment.Id,
            status,
            appointment.CancellationReason
                ?? throw new InvalidOperationException(
                    "Cancelled appointment must have a reason."),
            appointment.CancelledAt
                ?? throw new InvalidOperationException(
                    "Cancelled appointment must have a timestamp."),
            appointment.UpdatedAt);
    }

    private static string ComputeHash(
        CancelAppointmentCommand command)
    {
        var json = JsonSerializer.Serialize(new
        {
            command.AppointmentId,
            command.CancellationStatus,
            command.Reason
        });

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static CancelAppointmentResult Replay(
        IdempotencyRecord record,
        string requestHash)
    {
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            return Failure(
                CancelAppointmentError.IdempotencyConflict);
        }

        var response =
            JsonSerializer.Deserialize<CancelAppointmentResponse>(
                record.ResponseJson)
            ?? throw new InvalidOperationException(
                "Stored cancellation response could not be read.");

        return CancelAppointmentResult.Success(response);
    }

    private void DetachAttempt(
        Appointment? appointment,
        IdempotencyRecord? record,
        AppointmentHistoryEntry? historyEntry)
    {
        if (historyEntry is not null)
        {
            _dbContext.Entry(historyEntry).State = EntityState.Detached;
        }

        if (record is not null)
        {
            _dbContext.Entry(record).State = EntityState.Detached;
        }

        if (appointment is not null)
        {
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

    private static CancelAppointmentResult Failure(
        CancelAppointmentError error)
    {
        return CancelAppointmentResult.Failure(error);
    }
}