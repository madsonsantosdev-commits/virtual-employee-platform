using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VirtualEmployee.Application.Payments.ConfirmPayment;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Persistence.Idempotency;

namespace VirtualEmployee.Infrastructure.Payments;

public sealed class PaymentConfirmationService
    : IPaymentConfirmationService
{
    public const string Operation = "ConfirmPayment";

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PaymentConfirmationService(
        AppDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ConfirmPaymentResult> ConfirmAsync(
        ConfirmPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsValid(command))
        {
            return Failure(ConfirmPaymentOutcome.InvalidNotification);
        }

        command = command with
        {
            ApprovedAt = ToMicroseconds(command.ApprovedAt)
        };

        var key = ComputeHash(new
        {
            command.Provider,
            command.ProviderPaymentId
        });

        var requestHash = ComputeHash(command);
        var tenantId = _dbContext.CurrentTenantId;

        Appointment? appointment = null;
        Payment? payment = null;
        PaymentAttempt? attempt = null;
        AppointmentHistoryEntry? history = null;
        IdempotencyRecord? record = null;

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            var confirmationLock =
                $"{tenantId:D}:{Operation}:{key}";

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({confirmationLock}, 0));",
                cancellationToken);

            var existingRecord = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity =>
                        entity.Operation == Operation &&
                        entity.Key == key,
                    cancellationToken);

            if (existingRecord is not null)
            {
                if (!string.Equals(
                        existingRecord.RequestHash,
                        requestHash,
                        StringComparison.Ordinal))
                {
                    return Failure(
                        ConfirmPaymentOutcome.IdempotencyConflict);
                }

                var stored =
                    JsonSerializer.Deserialize<StoredConfirmation>(
                        existingRecord.ResponseJson)
                    ?? throw new InvalidOperationException(
                        "Stored payment confirmation could not be read.");

                return stored.Result;
            }

            var attemptReference = await _dbContext.PaymentAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.Id == command.PaymentAttemptId,
                    cancellationToken);

            if (attemptReference is null)
            {
                return Failure(ConfirmPaymentOutcome.NotFound);
            }

            var paymentReference = await _dbContext.Payments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.Id == attemptReference.PaymentId,
                    cancellationToken);

            if (paymentReference is null)
            {
                return Failure(ConfirmPaymentOutcome.NotFound);
            }

            // Mesmo escopo usado pela preparação e conclusão
            // das tentativas deste agendamento.
            var appointmentLock =
                $"{tenantId:D}:PaymentAttempts:" +
                $"{paymentReference.AppointmentId:D}";

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({appointmentLock}, 0));",
                cancellationToken);

            appointment = await _dbContext.Appointments
                .SingleOrDefaultAsync(
                    entity =>
                        entity.Id == paymentReference.AppointmentId,
                    cancellationToken);

            payment = await _dbContext.Payments
                .SingleOrDefaultAsync(
                    entity => entity.Id == paymentReference.Id,
                    cancellationToken);

            attempt = await _dbContext.PaymentAttempts
                .SingleOrDefaultAsync(
                    entity => entity.Id == command.PaymentAttemptId,
                    cancellationToken);

            if (appointment is null ||
                payment is null ||
                attempt is null)
            {
                return Failure(ConfirmPaymentOutcome.NotFound);
            }

            if (attempt.PaymentId != payment.Id ||
                payment.AppointmentId != appointment.Id ||
                payment.BusinessId != appointment.BusinessId ||
                command.Amount != attempt.Amount ||
                command.Amount != payment.Amount ||
                command.Amount != appointment.TotalPriceSnapshot)
            {
                return Failure(
                    ConfirmPaymentOutcome.InvalidNotification);
            }

            if (attempt.Provider is not null &&
                (!string.Equals(
                    attempt.Provider,
                    command.Provider,
                    StringComparison.Ordinal) ||
                 !string.Equals(
                    attempt.ProviderPaymentId,
                    command.ProviderOrderId,
                    StringComparison.Ordinal)))
            {
                return Failure(
                    ConfirmPaymentOutcome.InvalidNotification);
            }

            var now = ToMicroseconds(_timeProvider.GetUtcNow());

            if (now < appointment.UpdatedAt ||
                now < payment.UpdatedAt ||
                now < attempt.UpdatedAt)
            {
                return Failure(
                    ConfirmPaymentOutcome.ConcurrencyConflict);
            }

            if (command.ApprovedAt > now)
            {
                return Failure(
                    ConfirmPaymentOutcome.InvalidNotification);
            }

            // Pode ocorrer quando o webhook chega antes da conclusão
            // local da criação da order.
            if (attempt.Provider is null)
            {
                attempt.RegisterProviderPayment(
                    command.Provider,
                    command.ProviderOrderId,
                    now);
            }

            var alreadySettled = payment.Status is
                PaymentStatus.Confirmed or PaymentStatus.Refunded;

            attempt.Confirm(command.Amount, now);

            ConfirmPaymentOutcome outcome;

            if (alreadySettled)
            {
                // Uma nova confirmação financeira não substitui
                // a tentativa que liquidou o Payment.
                outcome = ConfirmPaymentOutcome.AdditionalPayment;
            }
            else
            {
                payment.Confirm(attempt, now);

                if (appointment.Status == AppointmentStatus.Pending &&
                    appointment.ReservationExpiresAt is { } deadline &&
                    now >= deadline)
                {
                    var before =
                        AppointmentHistoryState.Capture(appointment);

                    appointment.Expire(now);

                    history = CreateHistory(
                        appointment,
                        AppointmentHistoryType.Expired,
                        before,
                        now);

                    outcome = ConfirmPaymentOutcome.ReservationExpired;
                }
                else if (appointment.Status == AppointmentStatus.Pending)
                {
                    var before =
                        AppointmentHistoryState.Capture(appointment);

                    appointment.ConfirmPayment(payment, now);

                    history = CreateHistory(
                        appointment,
                        AppointmentHistoryType.PaymentConfirmed,
                        before,
                        now);

                    outcome = ConfirmPaymentOutcome.Confirmed;
                }
                else if (
                    appointment.Status == AppointmentStatus.Confirmed)
                {
                    outcome = ConfirmPaymentOutcome.Confirmed;
                }
                else if (
                    appointment.Status == AppointmentStatus.Expired)
                {
                    outcome = ConfirmPaymentOutcome.ReservationExpired;
                }
                else
                {
                    outcome =
                        ConfirmPaymentOutcome.AppointmentNotConfirmable;
                }
            }

            var result = new ConfirmPaymentResult(
                outcome,
                payment.Id,
                appointment.Id);

            record = new IdempotencyRecord(
                tenantId,
                Operation,
                key,
                requestHash,
                JsonSerializer.Serialize(
                    new StoredConfirmation(command, result)),
                now);

            if (history is not null)
            {
                _dbContext.AppointmentHistoryEntries.Add(history);
            }

            _dbContext.IdempotencyRecords.Add(record);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(
                ConfirmPaymentOutcome.ConcurrencyConflict);
        }
        catch (Exception exception) when (
            FindPostgresException(exception)?.SqlState is
                PostgresErrorCodes.DeadlockDetected or
                PostgresErrorCodes.UniqueViolation)
        {
            return Failure(
                ConfirmPaymentOutcome.ConcurrencyConflict);
        }
        finally
        {
            Detach(record);
            Detach(history);
            Detach(attempt);
            Detach(payment);
            Detach(appointment);
        }
    }

    private static AppointmentHistoryEntry CreateHistory(
        Appointment appointment,
        AppointmentHistoryType type,
        AppointmentHistoryState before,
        DateTimeOffset now)
    {
        return new AppointmentHistoryEntry(
            Guid.NewGuid(),
            appointment.TenantId,
            appointment.Id,
            type,
            now,
            before,
            AppointmentHistoryState.Capture(appointment));
    }

    private static bool IsValid(ConfirmPaymentCommand command)
    {
        return command.PaymentAttemptId != Guid.Empty &&
            IsValidReference(command.Provider, 100) &&
            IsValidReference(command.ProviderOrderId, 200) &&
            IsValidReference(command.ProviderPaymentId, 200) &&
            command.Currency == "BRL" &&
            command.Amount > 0 &&
            command.Amount <= 9_999_999_999.99m &&
            decimal.Round(command.Amount, 2) == command.Amount &&
            command.ApprovedAt != default;
    }

    private static bool IsValidReference(
        string? value,
        int maximumLength)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Length <= maximumLength &&
            !value.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character));
    }

    private static string ComputeHash<T>(T value)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(value))));
    }

    private static DateTimeOffset ToMicroseconds(
        DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();

        return new DateTimeOffset(
            utc.Ticks - utc.Ticks % 10,
            TimeSpan.Zero);
    }

    private void Detach(object? entity)
    {
        if (entity is not null)
        {
            _dbContext.Entry(entity).State = EntityState.Detached;
        }
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

    private static ConfirmPaymentResult Failure(
        ConfirmPaymentOutcome outcome)
    {
        return new ConfirmPaymentResult(outcome, null, null);
    }

    private sealed record StoredConfirmation(
        ConfirmPaymentCommand Confirmation,
        ConfirmPaymentResult Result);
}