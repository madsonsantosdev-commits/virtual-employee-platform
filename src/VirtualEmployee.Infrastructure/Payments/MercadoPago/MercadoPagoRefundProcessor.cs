using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoRefundProcessor(
    AppDbContext database,
    MercadoPagoOrdersClient orders,
    MercadoPagoAccountResolver accounts,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<bool> ProcessAsync(Guid businessId, Guid refundId, CancellationToken cancellationToken)
    {
        var refund = await database.Refunds.SingleOrDefaultAsync(
            entity => entity.Id == refundId, cancellationToken);
        if (refund is null || refund.Status is not (RefundStatus.Requested or RefundStatus.Processing))
        {
            return false;
        }

        var payment = await database.Payments.SingleOrDefaultAsync(
            entity => entity.Id == refund.PaymentId && entity.BusinessId == businessId, cancellationToken);
        var isAdditional = refund.Reason == RefundReason.AdditionalPayment;
        if (payment is null ||
            (payment.Status != PaymentStatus.Confirmed &&
                !(isAdditional && payment.Status == PaymentStatus.Refunded)) ||
            payment.ConfirmedPaymentAttemptId is null ||
            (payment.ConfirmedPaymentAttemptId == refund.PaymentAttemptId) == isAdditional ||
            payment.Amount != refund.Amount)
        {
            return false;
        }

        var attempt = await database.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(
            entity => entity.Id == refund.PaymentAttemptId &&
                entity.PaymentId == payment.Id && entity.Status == PaymentAttemptStatus.Confirmed &&
                entity.Provider == "MERCADO_PAGO", cancellationToken);
        if (attempt?.ProviderPaymentId is not { } orderId)
        {
            return false;
        }

        var account = accounts.ResolveForConfirmation(refund.TenantId, businessId);
        using var current = await orders.GetAsync(account.AccessToken, orderId, cancellationToken);
        current.EnsureSuccessStatusCode();
        var currentJson = await current.Content.ReadAsStringAsync(cancellationToken);

        var reference = MercadoPagoRefundReader.ReadCompleted(
            currentJson, orderId, refund.Amount, attempt.Id);

        if (reference is null && MercadoPagoProcessedOrderReader.Read(
            currentJson, orderId, account.CollectorId, account.ApplicationId, attempt.Id, refund.Amount,
            requireNoRefunds: true) is null)
        {
            return false;
        }

        if (refund.Status == RefundStatus.Requested)
        {
            refund.MarkProcessing(_clock.GetUtcNow());
            await database.SaveChangesAsync(cancellationToken);
        }

        if (reference is null)
        {
            using var response = await orders.RefundAsync(
                account.AccessToken, orderId, refund.Id, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            reference = MercadoPagoRefundReader.ReadCompleted(json, orderId, refund.Amount);
            if (reference is null)
            {
                return false;
            }
        }

        var now = _clock.GetUtcNow();
        refund.Complete(reference, now);
        if (!isAdditional)
        {
            payment.MarkRefunded(refund, now);
        }
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
