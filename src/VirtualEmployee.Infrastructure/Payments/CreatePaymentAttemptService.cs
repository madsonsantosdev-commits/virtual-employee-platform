using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Payments.CreatePaymentAttempt;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Payments;

public sealed class CreatePaymentAttemptService : ICreatePaymentAttemptService
{
    private readonly AppDbContext _dbContext;
    private readonly IPaymentGateway _gateway;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentAttemptPreparationService _preparation;
    private readonly PaymentAttemptCompletionService _completion;

    public CreatePaymentAttemptService(
        AppDbContext dbContext,
        IPaymentGateway gateway,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(gateway);

        _dbContext = dbContext;
        _gateway = gateway;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _preparation = new PaymentAttemptPreparationService(
            dbContext,
            _timeProvider);

        _completion = new PaymentAttemptCompletionService(
            dbContext,
            _timeProvider);
    }

    public async Task<CreatePaymentAttemptResult> CreateAsync(
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

        EnsureNoTransaction();

        var replay = await _completion.TryReplayAsync(
            command,
            cancellationToken);

        if (replay is not null)
        {
            return replay;
        }

        var prepared = await _preparation.PrepareAsync(
            command,
            cancellationToken);

        if (!prepared.IsSuccess)
        {
            // Outra execução pode ter concluído durante a preparação.
            replay = await _completion.TryReplayAsync(
                command,
                cancellationToken);

            return replay ?? Failure(prepared.Error);
        }

        var request = prepared.Request
            ?? throw new InvalidOperationException(
                "Successful preparation must contain a gateway request.");

        replay = await _completion.TryReplayAsync(
            command,
            cancellationToken);

        if (replay is not null)
        {
            return replay;
        }

        var validationError = await ValidateBeforeGatewayAsync(
            request,
            cancellationToken);

        if (validationError != CreatePaymentAttemptError.None)
        {
            replay = await _completion.TryReplayAsync(
                command,
                cancellationToken);

            return replay ?? Failure(validationError);
        }

        EnsureNoTransaction();
        cancellationToken.ThrowIfCancellationRequested();

        CreateGatewayPaymentResult gatewayResult;

        try
        {
            gatewayResult = await _gateway.CreateAsync(
                request,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return Failure(CreatePaymentAttemptError.ProviderError);
        }
        catch (HttpRequestException)
        {
            return Failure(CreatePaymentAttemptError.ProviderError);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(CreatePaymentAttemptError.ProviderError);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (gatewayResult is null)
        {
            return Failure(CreatePaymentAttemptError.ProviderError);
        }

        return await _completion.CompleteAsync(
            command,
            request,
            gatewayResult,
            cancellationToken);
    }

    private async Task<CreatePaymentAttemptError> ValidateBeforeGatewayAsync(
        CreateGatewayPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == request.AppointmentId,
                cancellationToken);

        if (appointment is null)
        {
            return CreatePaymentAttemptError.AppointmentNotFound;
        }

        var now = GetUtcNow();

        if (now < appointment.UpdatedAt)
        {
            return CreatePaymentAttemptError.ConcurrencyConflict;
        }

        if (appointment.Status == AppointmentStatus.Expired)
        {
            return CreatePaymentAttemptError.ReservationExpired;
        }

        if (appointment.Status != AppointmentStatus.Pending)
        {
            return CreatePaymentAttemptError.InvalidAppointmentState;
        }

        if (appointment.ReservationExpiresAt is not { } expiresAt ||
            now >= expiresAt ||
            now >= request.ExpiresAt)
        {
            return CreatePaymentAttemptError.ReservationExpired;
        }

        var payment = await _dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == request.PaymentId,
                cancellationToken);

        var attempt = await _dbContext.PaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == request.PaymentAttemptId,
                cancellationToken);

        if (payment is null ||
            attempt is null ||
            payment.AppointmentId != appointment.Id ||
            payment.BusinessId != request.BusinessId ||
            appointment.BusinessId != request.BusinessId ||
            attempt.PaymentId != payment.Id ||
            payment.Amount != request.Amount ||
            attempt.Amount != request.Amount ||
            attempt.PaymentMethod != request.PaymentMethod)
        {
            return CreatePaymentAttemptError.ConcurrencyConflict;
        }

        if (now < payment.UpdatedAt || now < attempt.UpdatedAt)
        {
            return CreatePaymentAttemptError.ConcurrencyConflict;
        }

        if (payment.Status is
            PaymentStatus.Confirmed or PaymentStatus.Refunded)
        {
            return CreatePaymentAttemptError.PaymentAlreadyConfirmed;
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return CreatePaymentAttemptError.InvalidAppointmentState;
        }

        if (attempt.Status != PaymentAttemptStatus.Created)
        {
            return CreatePaymentAttemptError.ConcurrencyConflict;
        }

        return CreatePaymentAttemptError.None;
    }

    private void EnsureNoTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is not null ||
            System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException(
                "Payment creation must run outside an existing transaction.");
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        var utc = _timeProvider.GetUtcNow().ToUniversalTime();

        return new DateTimeOffset(
            utc.Ticks - utc.Ticks % 10,
            TimeSpan.Zero);
    }

    private static CreatePaymentAttemptResult Failure(
        CreatePaymentAttemptError error)
    {
        return CreatePaymentAttemptResult.Failure(error);
    }
}