using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Api.Configuration;

public sealed class MercadoPagoRefundWorker(
    IServiceScopeFactory scopes,
    MercadoPagoOptions options,
    ILogger<MercadoPagoRefundWorker> logger) : BackgroundService
{
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cursors = new Dictionary<(Guid TenantId, Guid BusinessId), Guid>();
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var account in options.Accounts)
            {
                var key = (account.TenantId, account.BusinessId);
                try
                {
                    var batch = await ReadBatchAsync(account, cursors.GetValueOrDefault(key), stoppingToken);
                    foreach (var refundId in batch)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        await ProcessAsync(account, refundId, stoppingToken);
                        cursors[key] = refundId;
                    }

                    if (batch.Count < BatchSize)
                    {
                        cursors.Remove(key);
                    }
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(
                        "Mercado Pago refund batch failed for tenant {TenantId}, business {BusinessId}: {ErrorType}",
                        account.TenantId, account.BusinessId, error.GetType().Name);
                }
            }
        }
    }

    private async Task<List<Guid>> ReadBatchAsync(
        MercadoPagoAccountOptions account, Guid cursor, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextInitializer>().Initialize(account.TenantId);
        var database = services.GetRequiredService<AppDbContext>();
        return await (
            from refund in database.Refunds
            join payment in database.Payments on refund.PaymentId equals payment.Id
            join attempt in database.PaymentAttempts on refund.PaymentAttemptId equals attempt.Id
            where payment.BusinessId == account.BusinessId &&
                payment.Status == PaymentStatus.Confirmed &&
                attempt.Provider == "MERCADO_PAGO" &&
                (refund.Status == RefundStatus.Requested || refund.Status == RefundStatus.Processing) &&
                (cursor == Guid.Empty || refund.Id.CompareTo(cursor) > 0)
            orderby refund.Id
            select refund.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task ProcessAsync(
        MercadoPagoAccountOptions account, Guid refundId, CancellationToken stoppingToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ITenantContextInitializer>().Initialize(account.TenantId);
            var processor = services.GetRequiredService<MercadoPagoRefundProcessor>();
            if (await processor.ProcessAsync(account.BusinessId, refundId, deadline.Token))
            {
                logger.LogInformation("Mercado Pago refund {RefundId} completed.", refundId);
            }
            else
            {
                logger.LogDebug("Mercado Pago refund {RefundId} has no final confirmation.", refundId);
            }
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Mercado Pago refund {RefundId} failed to reconcile: {ErrorType}",
                refundId, error.GetType().Name);
        }
    }
}
