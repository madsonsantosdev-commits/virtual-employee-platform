using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VirtualEmployee.Application.Payments.CreatePaymentAttempt;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Persistence.Idempotency;

namespace VirtualEmployee.Infrastructure.Payments;

public sealed class PaymentAttemptPreparationService
{
    public const string IntentOperation = "CreatePaymentAttemptIntent";

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PaymentAttemptPreparationService(
        AppDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PaymentAttemptPreparationResult> PrepareAsync(
        CreatePaymentAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AppointmentId == Guid.Empty ||
            !Enum.IsDefined(command.PaymentMethod) ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Length > 200 ||
            command.IdempotencyKey.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            return Failure(CreatePaymentAttemptError.InvalidRequest);
        }

        var requestHash = PaymentAttemptRequestHasher.Compute(command);
        var tenantId = _dbContext.CurrentTenantId;

        var keyLock =
            $"{tenantId:D}:{IntentOperation}:{command.IdempotencyKey}";

        var appointmentLock =
            $"{tenantId:D}:PaymentAttempts:{command.AppointmentId:D}";

        Payment? newPayment = null;
        PaymentAttempt? newAttempt = null;
        IdempotencyRecord? newIntent = null;

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            // Serializa a mesma chave antes de consultar sua intenção.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({keyLock}, 0));",
                cancellationToken);

            // Serializa a criação do Payment e de novas tentativas
            // do mesmo agendamento, inclusive com chaves diferentes.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({appointmentLock}, 0));",
                cancellationToken);

            var existingIntent = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record =>
                        record.Operation == IntentOperation &&
                        record.Key == command.IdempotencyKey,
                    cancellationToken);

            if (existingIntent is not null &&
                !string.Equals(
                    existingIntent.RequestHash,
                    requestHash,
                    StringComparison.Ordinal))
            {
                return Failure(
                    CreatePaymentAttemptError.IdempotencyConflict);
            }

            var appointment = await _dbContext.Appointments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.Id == command.AppointmentId,
                    cancellationToken);

            if (appointment is null)
            {
                return Failure(
                    CreatePaymentAttemptError.AppointmentNotFound);
            }

            var now = GetUtcNow();

            if (now < appointment.UpdatedAt)
            {
                return Failure(
                    CreatePaymentAttemptError.ConcurrencyConflict);
            }

            var payment = await _dbContext.Payments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.AppointmentId == appointment.Id,
                    cancellationToken);

            if (payment is not null &&
                payment.Status is
                    (PaymentStatus.Confirmed or PaymentStatus.Refunded))
            {
                return Failure(
                    CreatePaymentAttemptError.PaymentAlreadyConfirmed);
            }

            if (appointment.Status == AppointmentStatus.Expired)
            {
                return Failure(
                    CreatePaymentAttemptError.ReservationExpired);
            }

            if (appointment.Status != AppointmentStatus.Pending)
            {
                return Failure(
                    CreatePaymentAttemptError.InvalidAppointmentState);
            }

            if (appointment.ReservationExpiresAt is not { } expiresAt ||
                now >= expiresAt)
            {
                return Failure(
                    CreatePaymentAttemptError.ReservationExpired);
            }

            if (existingIntent is not null)
            {
                var originalRequest =
                    JsonSerializer.Deserialize<CreateGatewayPaymentRequest>(
                        existingIntent.ResponseJson)
                    ?? throw new InvalidOperationException(
                        "Stored payment attempt intent could not be read.");

                if (originalRequest.TenantId != tenantId ||
                    originalRequest.AppointmentId != command.AppointmentId ||
                    originalRequest.PaymentMethod != command.PaymentMethod)
                {
                    throw new InvalidOperationException(
                        "Stored payment attempt intent is inconsistent.");
                }

                // A preparação não recria a tentativa nem recalcula
                // os parâmetros que serão enviados ao gateway.
                await transaction.CommitAsync(cancellationToken);

                return PaymentAttemptPreparationResult.Success(
                    originalRequest);
            }

            if (payment is not null)
            {
                if (payment.Status is
                    PaymentStatus.Confirmed or PaymentStatus.Refunded)
                {
                    return Failure(
                        CreatePaymentAttemptError.PaymentAlreadyConfirmed);
                }

                if (payment.Status == PaymentStatus.Cancelled)
                {
                    return Failure(
                        CreatePaymentAttemptError.InvalidAppointmentState);
                }

                if (now < payment.UpdatedAt)
                {
                    return Failure(
                        CreatePaymentAttemptError.ConcurrencyConflict);
                }

                // Uma tentativa ainda sem resultado externo conhecido
                // deve ser recuperada com sua chave original.
                var unresolvedAttempt =
                    await _dbContext.PaymentAttempts.AnyAsync(
                        entity =>
                            entity.PaymentId == payment.Id &&
                            entity.Status == PaymentAttemptStatus.Created,
                        cancellationToken);

                if (unresolvedAttempt)
                {
                    return Failure(
                        CreatePaymentAttemptError.ConcurrencyConflict);
                }
            }
            else
            {
                var amount = appointment.TotalPriceSnapshot;

                if (amount <= 0 ||
                    amount > 9_999_999_999.99m ||
                    decimal.Round(amount, 2) != amount)
                {
                    return Failure(
                        CreatePaymentAttemptError.InvalidAppointmentState);
                }

                newPayment = new Payment(
                    Guid.NewGuid(),
                    appointment,
                    now);

                payment = newPayment;
                _dbContext.Payments.Add(newPayment);
            }

            newAttempt = new PaymentAttempt(
                Guid.NewGuid(),
                payment,
                command.PaymentMethod,
                now,
                expiresAt);

            var gatewayRequest = new CreateGatewayPaymentRequest(
                tenantId,
                appointment.BusinessId,
                appointment.Id,
                payment.Id,
                newAttempt.Id,
                payment.Amount,
                command.PaymentMethod,
                expiresAt);

            newIntent = new IdempotencyRecord(
                tenantId,
                IntentOperation,
                command.IdempotencyKey,
                requestHash,
                JsonSerializer.Serialize(gatewayRequest),
                now);

            _dbContext.PaymentAttempts.Add(newAttempt);
            _dbContext.IdempotencyRecords.Add(newIntent);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return PaymentAttemptPreparationResult.Success(gatewayRequest);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure(
                CreatePaymentAttemptError.ConcurrencyConflict);
        }
        catch (Exception exception) when (
            FindPostgresException(exception)?.SqlState is
                PostgresErrorCodes.UniqueViolation or
                PostgresErrorCodes.DeadlockDetected)
        {
            return Failure(
                CreatePaymentAttemptError.ConcurrencyConflict);
        }
        finally
        {
            // O próximo estágio deve consultar uma versão atualizada.
            if (newIntent is not null)
            {
                _dbContext.Entry(newIntent).State = EntityState.Detached;
            }

            if (newAttempt is not null)
            {
                _dbContext.Entry(newAttempt).State = EntityState.Detached;
            }

            if (newPayment is not null)
            {
                _dbContext.Entry(newPayment).State = EntityState.Detached;
            }
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

    private static PaymentAttemptPreparationResult Failure(
        CreatePaymentAttemptError error)
    {
        return PaymentAttemptPreparationResult.Failure(error);
    }
}