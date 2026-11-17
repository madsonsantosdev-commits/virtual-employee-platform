using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VirtualEmployee.Application.Payments.RequestRefund;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Persistence.Idempotency;

namespace VirtualEmployee.Infrastructure.Payments;

public sealed class RequestRefundService(
    AppDbContext database,
    IConfiguration configuration,
    TimeProvider? timeProvider = null) : IRequestRefundService
{
    private const string Operation = "RequestRefund";
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<RequestRefundResult> RequestAsync(
        RequestRefundCommand command,
        CancellationToken cancellationToken)
    {
        var tenantId = database.CurrentTenantId;
        var requestHash = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { command.PaymentId, command.Reason })));

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockAsync($"{tenantId:D}:{Operation}:{command.IdempotencyKey}", cancellationToken);
            var replay = await database.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
                record => record.Operation == Operation && record.Key == command.IdempotencyKey,
                cancellationToken);

            if (replay is not null)
            {
                return replay.RequestHash == requestHash
                    ? RequestRefundResult.Accepted(
                        JsonSerializer.Deserialize<RequestRefundResponse>(replay.ResponseJson)
                        ?? throw new InvalidOperationException("Stored refund response is missing."))
                    : RequestRefundResult.Rejected(RequestRefundError.IdempotencyConflict);
            }

            await LockAsync($"{tenantId:D}:RefundPayment:{command.PaymentId:D}", cancellationToken);

            var payment = await database.Payments.AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == command.PaymentId, cancellationToken);
            if (payment is null)
            {
                return RequestRefundResult.Rejected(RequestRefundError.NotFound);
            }

            var existing = await database.Refunds.AsNoTracking()
                .Where(refund => refund.PaymentId == payment.Id && refund.Status != RefundStatus.Failed)
                .Select(refund => (RefundStatus?)refund.Status)
                .SingleOrDefaultAsync(cancellationToken);

            if (payment.Status == PaymentStatus.Refunded || existing == RefundStatus.Refunded)
            {
                return RequestRefundResult.Rejected(RequestRefundError.AlreadyCompleted);
            }

            if (existing is not null)
            {
                return RequestRefundResult.Rejected(RequestRefundError.InProgress);
            }

            if (payment.Status != PaymentStatus.Confirmed ||
                payment.ConfirmedPaymentAttemptId is not { } attemptId)
            {
                return RequestRefundResult.Rejected(RequestRefundError.NotAllowed);
            }

            var appointmentStatus = await database.Appointments.AsNoTracking()
                .Where(appointment => appointment.Id == payment.AppointmentId)
                .Select(appointment => (AppointmentStatus?)appointment.Status)
                .SingleOrDefaultAsync(cancellationToken);

            if (!AllowsRefund(command.Reason, appointmentStatus))
            {
                return RequestRefundResult.Rejected(RequestRefundError.NotAllowed);
            }

            var attempt = await database.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(
                entity => entity.Id == attemptId && entity.PaymentId == payment.Id,
                cancellationToken);
            if (attempt is null || attempt.Status != PaymentAttemptStatus.Confirmed)
            {
                return RequestRefundResult.Rejected(RequestRefundError.NotAllowed);
            }

            var now = _clock.GetUtcNow();
            if (now < payment.UpdatedAt || now < attempt.UpdatedAt)
            {
                return RequestRefundResult.Rejected(RequestRefundError.ConcurrencyConflict);
            }

            var refund = new Refund(Guid.NewGuid(), payment, attempt, command.Reason, now);
            var response = new RequestRefundResponse(
                refund.Id, payment.Id, attempt.Id, refund.Amount, "REFUND_REQUESTED");

            database.Refunds.Add(refund);
            database.IdempotencyRecords.Add(new IdempotencyRecord(
                tenantId, Operation, command.IdempotencyKey, requestHash,
                JsonSerializer.Serialize(response), now));
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return RequestRefundResult.Accepted(response);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_refunds_tenant_id_payment_id"
            })
        {
            return RequestRefundResult.Rejected(RequestRefundError.InProgress);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.DeadlockDetected)
        {
            return RequestRefundResult.Rejected(RequestRefundError.ConcurrencyConflict);
        }
    }

    private Task LockAsync(string key, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0));", cancellationToken);

    private bool AllowsRefund(RefundReason reason, AppointmentStatus? status) => reason switch
    {
        RefundReason.BusinessCancellation => status == AppointmentStatus.CancelledByBusiness,
        RefundReason.CustomerCancellation => status == AppointmentStatus.CancelledByClient &&
            bool.TryParse(configuration["Payments:Refunds:AllowCustomerCancellation"], out var allowed) && allowed,
        RefundReason.LatePayment => status == AppointmentStatus.Expired,
        _ => false
    };
}
