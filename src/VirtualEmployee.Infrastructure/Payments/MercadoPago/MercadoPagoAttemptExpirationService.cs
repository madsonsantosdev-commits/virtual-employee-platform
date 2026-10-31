using System.Net;
using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoAttemptExpirationService(
    AppDbContext database,
    MercadoPagoAccountResolver resolver,
    MercadoPagoOrdersClient orders,
    TimeProvider timeProvider)
{
    public async Task<bool> TryExpireAsync(
        Guid businessId, Guid attemptId, string orderId,
        CancellationToken cancellationToken = default)
    {
        var reference = await (
            from attempt in database.PaymentAttempts.AsNoTracking()
            join payment in database.Payments.AsNoTracking()
                on attempt.PaymentId equals payment.Id
            where attempt.Id == attemptId && payment.BusinessId == businessId &&
                attempt.Provider == "MERCADO_PAGO" && attempt.ProviderPaymentId == orderId
            select new { attempt.Amount, payment.AppointmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (reference is null) return false;

        var account = resolver.ResolveForConfirmation(database.CurrentTenantId, businessId);
        // Never hold a database transaction during provider I/O.
        using var response = await orders.GetAsync(account.AccessToken, orderId, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) return false;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!MercadoPagoExpiredOrderReader.IsExpired(json, orderId,
                account.CollectorId, account.ApplicationId, attemptId, reference.Amount))
            return false;

        PaymentAttempt? entity = null;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            // Serialize with preparation, completion and financial confirmation.
            var key = $"{database.CurrentTenantId:D}:PaymentAttempts:{reference.AppointmentId:D}";
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0));", cancellationToken);

            entity = await database.PaymentAttempts.SingleOrDefaultAsync(
                attempt => attempt.Id == attemptId, cancellationToken);
            if (entity is null || entity.Provider != "MERCADO_PAGO" ||
                entity.ProviderPaymentId != orderId || entity.Amount != reference.Amount)
                return false;

            // A stale expiration notification must never reverse financial truth.
            if (entity.Status is PaymentAttemptStatus.Confirmed or
                PaymentAttemptStatus.Expired or PaymentAttemptStatus.Cancelled or
                PaymentAttemptStatus.Failed)
                return true;

            var utc = timeProvider.GetUtcNow().ToUniversalTime();
            var now = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
            if (now < entity.UpdatedAt || !entity.Expire(now)) return false;

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            // Payment and Appointment are intentionally unchanged.
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            if (entity is not null) database.Entry(entity).State = EntityState.Detached;
        }
    }
}
