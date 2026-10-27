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

public sealed class PaymentAttemptCompletionService
{
    public const string Operation = "CreatePaymentAttempt";

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PaymentAttemptCompletionService(
        AppDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CreatePaymentAttemptResult?> TryReplayAsync(
        CreatePaymentAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var record = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity =>
                    entity.Operation == Operation &&
                    entity.Key == command.IdempotencyKey,
                cancellationToken);

        if (record is null)
        {
            return null;
        }

        var requestHash = PaymentAttemptRequestHasher.Compute(command);

        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            return Failure(
                CreatePaymentAttemptError.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<StoredResult>(
            record.ResponseJson)
            ?? throw new InvalidOperationException(
                "Stored payment attempt result could not be read.");

        if (stored.Error == CreatePaymentAttemptError.None &&
            stored.Response is { } response)
        {
            return CreatePaymentAttemptResult.Success(response);
        }

        if (stored.Error != CreatePaymentAttemptError.None &&
            stored.Response is null &&
            Enum.IsDefined(stored.Error))
        {
            return Failure(stored.Error);
        }

        throw new InvalidOperationException(
            "Stored payment attempt result is inconsistent.");
    }

    public async Task<CreatePaymentAttemptResult> CompleteAsync(
        CreatePaymentAttemptCommand command,
        CreateGatewayPaymentRequest request,
        CreateGatewayPaymentResult gatewayResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(gatewayResult);

        if (gatewayResult.Outcome == CreateGatewayPaymentOutcome.Unknown)
        {
            return Failure(
                CreatePaymentAttemptError.ProviderError);
        }

        if (gatewayResult.Outcome is not
            (CreateGatewayPaymentOutcome.Created or
             CreateGatewayPaymentOutcome.Rejected))
        {
            return Failure(
                CreatePaymentAttemptError.ProviderError);
        }

        if (request.TenantId != _dbContext.CurrentTenantId ||
            request.AppointmentId != command.AppointmentId ||
            request.PaymentMethod != command.PaymentMethod)
        {
            throw new ArgumentException(
                "Gateway request does not match the current operation.",
                nameof(request));
        }

        var tenantId = _dbContext.CurrentTenantId;
        var requestHash = PaymentAttemptRequestHasher.Compute(command);

        var keyLock =
            $"{tenantId:D}:" +
            $"{PaymentAttemptPreparationService.IntentOperation}:" +
            command.IdempotencyKey;

        var appointmentLock =
            $"{tenantId:D}:PaymentAttempts:{command.AppointmentId:D}";

        PaymentAttempt? attempt = null;
        IdempotencyRecord? finalRecord = null;

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({keyLock}, 0));",
                cancellationToken);

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({appointmentLock}, 0));",
                cancellationToken);

            var replay = await TryReplayAsync(
                command,
                cancellationToken);

            if (replay is not null)
            {
                return replay;
            }

            var intent = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity =>
                        entity.Operation ==
                            PaymentAttemptPreparationService.IntentOperation &&
                        entity.Key == command.IdempotencyKey,
                    cancellationToken);

            if (intent is null)
            {
                return Failure(
                    CreatePaymentAttemptError.ConcurrencyConflict);
            }

            if (!string.Equals(
                    intent.RequestHash,
                    requestHash,
                    StringComparison.Ordinal))
            {
                return Failure(
                    CreatePaymentAttemptError.IdempotencyConflict);
            }

            var storedRequest =
                JsonSerializer.Deserialize<CreateGatewayPaymentRequest>(
                    intent.ResponseJson)
                ?? throw new InvalidOperationException(
                    "Stored payment attempt intent could not be read.");

            if (storedRequest != request)
            {
                throw new InvalidOperationException(
                    "Gateway request differs from the persisted intent.");
            }

            var appointment = await _dbContext.Appointments
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM appointments
                    WHERE tenant_id = {tenantId}
                      AND id = {request.AppointmentId}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);

            var payment = await _dbContext.Payments
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM payments
                    WHERE tenant_id = {tenantId}
                      AND id = {request.PaymentId}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);

            attempt = await _dbContext.PaymentAttempts
                .SingleOrDefaultAsync(
                    entity => entity.Id == request.PaymentAttemptId,
                    cancellationToken);

            if (appointment is null ||
                payment is null ||
                attempt is null ||
                payment.AppointmentId != appointment.Id ||
                attempt.PaymentId != payment.Id ||
                attempt.Amount != request.Amount ||
                attempt.PaymentMethod != request.PaymentMethod)
            {
                return Failure(
                    CreatePaymentAttemptError.ConcurrencyConflict);
            }

            var now = GetUtcNow();

            if (now < appointment.UpdatedAt ||
                now < payment.UpdatedAt ||
                now < attempt.UpdatedAt)
            {
                return Failure(
                    CreatePaymentAttemptError.ConcurrencyConflict);
            }

            CreatePaymentAttemptResult result;

            if (gatewayResult.Outcome ==
                CreateGatewayPaymentOutcome.Rejected)
            {
                if (attempt.Status == PaymentAttemptStatus.Confirmed)
                {
                    result = Failure(
                        CreatePaymentAttemptError.PaymentAlreadyConfirmed);
                }
                else
                {
                    if (attempt.Status is
                        PaymentAttemptStatus.Created or
                        PaymentAttemptStatus.Pending or
                        PaymentAttemptStatus.Processing)
                    {
                        attempt.Fail(now);
                    }

                    result = Failure(
                        CreatePaymentAttemptError.ProviderError);
                }
            }
            else
            {
                var reference = gatewayResult.Reference;

                if (reference is null)
                {
                    return Failure(
                        CreatePaymentAttemptError.ProviderError);
                }

                try
                {
                    attempt.RegisterProviderPayment(
                        reference.Provider,
                        reference.ProviderPaymentId,
                        now);
                }
                catch (ArgumentException)
                {
                    return Failure(
                        CreatePaymentAttemptError.ProviderError);
                }
                catch (InvalidOperationException)
                {
                    return Failure(
                        CreatePaymentAttemptError.ProviderError);
                }

                // Preserva a referência externa mesmo em resposta tardia.
                attempt.Expire(now);

                result = BuildCreatedResult(
                    appointment,
                    payment,
                    attempt,
                    reference,
                    now);
            }

            finalRecord = new IdempotencyRecord(
                tenantId,
                Operation,
                command.IdempotencyKey,
                requestHash,
                JsonSerializer.Serialize(
                    new StoredResult(result.Error, result.Response)),
                now);

            _dbContext.IdempotencyRecords.Add(finalRecord);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result;
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
            if (finalRecord is not null)
            {
                _dbContext.Entry(finalRecord).State =
                    EntityState.Detached;
            }

            if (attempt is not null)
            {
                _dbContext.Entry(attempt).State =
                    EntityState.Detached;
            }
        }
    }

    private static CreatePaymentAttemptResult BuildCreatedResult(
        Appointment appointment,
        Payment payment,
        PaymentAttempt attempt,
        GatewayPaymentReference reference,
        DateTimeOffset now)
    {
        if (payment.Status is
            PaymentStatus.Confirmed or PaymentStatus.Refunded)
        {
            return Failure(
                CreatePaymentAttemptError.PaymentAlreadyConfirmed);
        }

        if (appointment.Status == AppointmentStatus.Expired ||
            appointment.ReservationExpiresAt is not { } reservationExpiry ||
            now >= reservationExpiry ||
            attempt.Status == PaymentAttemptStatus.Expired)
        {
            return Failure(
                CreatePaymentAttemptError.ReservationExpired);
        }

        if (appointment.Status != AppointmentStatus.Pending ||
            payment.Status == PaymentStatus.Cancelled ||
            attempt.Status != PaymentAttemptStatus.Pending)
        {
            return Failure(
                CreatePaymentAttemptError.InvalidAppointmentState);
        }

        var effectiveExpiry = attempt.ExpiresAt;

        if (reservationExpiry < effectiveExpiry)
        {
            effectiveExpiry = reservationExpiry;
        }

        var providerExpiry = reference.ExpiresAt.ToUniversalTime();

        if (providerExpiry < effectiveExpiry)
        {
            effectiveExpiry = providerExpiry;
        }

        if (effectiveExpiry <= now)
        {
            return Failure(
                CreatePaymentAttemptError.ProviderError);
        }

        string? checkoutUrl = null;

        if (reference.CheckoutUrl is { } candidate &&
            Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo))
        {
            checkoutUrl = uri.AbsoluteUri;
        }

        var method = attempt.PaymentMethod switch
        {
            PaymentMethod.Pix => "PIX",
            PaymentMethod.CreditCard => "CREDIT_CARD",
            PaymentMethod.DebitCard => "DEBIT_CARD",
            _ => throw new InvalidOperationException(
                "Payment method is invalid.")
        };

        return CreatePaymentAttemptResult.Success(
            new CreatePaymentAttemptResponse(
                payment.Id,
                attempt.Id,
                method,
                "PENDING",
                attempt.Amount,
                checkoutUrl,
                effectiveExpiry));
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

    private static CreatePaymentAttemptResult Failure(
        CreatePaymentAttemptError error)
    {
        return CreatePaymentAttemptResult.Failure(error);
    }

    private sealed record StoredResult(
        CreatePaymentAttemptError Error,
        CreatePaymentAttemptResponse? Response);
}