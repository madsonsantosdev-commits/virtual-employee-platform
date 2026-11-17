using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Application.Payments.ConfirmPayment;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Api.Configuration;

public sealed class MercadoPagoReconciliationWorker(
    IServiceScopeFactory scopes,
    MercadoPagoOptions options,
    ILogger<MercadoPagoReconciliationWorker> logger) : BackgroundService
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
                    var batch = await ReadBatchAsync(
                        account, cursors.GetValueOrDefault(key), stoppingToken);

                    foreach (var candidate in batch)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        await ReconcileAsync(account, candidate, stoppingToken);
                        cursors[key] = candidate.Id;
                    }

                    if (batch.Count < BatchSize)
                    {
                        cursors.Remove(key);
                    }
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(
                        "Mercado Pago reconciliation batch failed for tenant {TenantId}, business {BusinessId}: {ErrorType}",
                        account.TenantId, account.BusinessId, error.GetType().Name);
                }
            }
        }
    }

    private async Task<List<Candidate>> ReadBatchAsync(
        MercadoPagoAccountOptions account, Guid cursor, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(account.TenantId);
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await (
            from attempt in database.PaymentAttempts.AsNoTracking()
            join payment in database.Payments.AsNoTracking()
                on attempt.PaymentId equals payment.Id
            where payment.BusinessId == account.BusinessId &&
                attempt.Provider == "MERCADO_PAGO" &&
                attempt.ProviderPaymentId != null &&
                (attempt.Status == PaymentAttemptStatus.Pending ||
                    attempt.Status == PaymentAttemptStatus.Processing) &&
                (attempt.PaymentMethod == PaymentMethod.CreditCard ||
                    attempt.PaymentMethod == PaymentMethod.DebitCard) &&
                (cursor == Guid.Empty || attempt.Id.CompareTo(cursor) > 0)
            orderby attempt.Id
            select new Candidate(
                attempt.Id, attempt.ProviderPaymentId!, attempt.Amount, attempt.PaymentMethod))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task ReconcileAsync(
        MercadoPagoAccountOptions account, Candidate candidate, CancellationToken stoppingToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(18));
        var cancellationToken = deadline.Token;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ITenantContextInitializer>().Initialize(account.TenantId);
            var verifier = services.GetRequiredService<MercadoPagoPaymentVerificationService>();
            var paymentType = candidate.Method == PaymentMethod.CreditCard
                ? "credit_card"
                : "debit_card";
            var command = await verifier.VerifyAsync(
                account.TenantId, account.BusinessId, candidate.Id, candidate.OrderId,
                candidate.Amount, paymentType, cancellationToken);

            if (command is null)
            {
                await services.GetRequiredService<MercadoPagoAttemptExpirationService>()
                    .TryExpireAsync(account.BusinessId, candidate.Id, candidate.OrderId, cancellationToken);
                return;
            }

            var result = await services.GetRequiredService<IPaymentConfirmationService>()
                .ConfirmAsync(command, cancellationToken);
            logger.LogInformation(
                "Mercado Pago reconciliation of attempt {AttemptId} returned {Outcome}",
                candidate.Id, result.Outcome);
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Mercado Pago reconciliation failed for attempt {AttemptId}: {ErrorType}",
                candidate.Id, error.GetType().Name);
        }
    }

    private sealed record Candidate(Guid Id, string OrderId, decimal Amount, PaymentMethod Method);
}
