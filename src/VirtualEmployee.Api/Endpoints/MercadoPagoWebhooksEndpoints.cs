using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Application.Payments.ConfirmPayment;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Api.Endpoints;

public static class MercadoPagoWebhooksEndpoints
{
    public static IEndpointRouteBuilder MapMercadoPagoWebhooksEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/webhooks/mercado-pago/{webhookId:guid}",
                HandleAsync)
            .WithName("ReceiveMercadoPagoWebhook")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid webhookId,
        HttpContext context,
        MercadoPagoWebhookAuthenticator authenticator,
        ITenantContextInitializer tenantInitializer)
    {
        // Authenticate the signed query identifier. Body fields are not used
        // as financial evidence or to choose the tenant.
        if (!TryGetSingle(context.Request.Query, "data.id", out var orderId) ||
            !TryGetSingleHeader(context, "x-signature", out var signature) ||
            !TryGetSingleHeader(context, "x-request-id", out var requestId))
        {
            return Results.Unauthorized();
        }

        var account = authenticator.Authenticate(
            webhookId, signature, requestId, orderId);

        if (account is null)
        {
            return Results.Unauthorized();
        }

        tenantInitializer.Initialize(account.TenantId);

        // Resolve tenant-scoped services only after tenant initialization.
        var database = context.RequestServices.GetRequiredService<AppDbContext>();
        var verifier = context.RequestServices
            .GetRequiredService<MercadoPagoPaymentVerificationService>();
        var confirmation = context.RequestServices
            .GetRequiredService<IPaymentConfirmationService>();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(18));
        var cancellationToken = deadline.Token;

        try
        {
            var matches = await (
                from attempt in database.PaymentAttempts.AsNoTracking()
                join payment in database.Payments.AsNoTracking()
                    on attempt.PaymentId equals payment.Id
                where attempt.Provider == "MERCADO_PAGO" &&
                    attempt.ProviderPaymentId == orderId &&
                    payment.BusinessId == account.BusinessId
                select new
                {
                    attempt.Id,
                    attempt.Amount,
                    attempt.PaymentMethod
                })
                .Take(2)
                .ToListAsync(cancellationToken);

            // The provider notification can precede local order completion.
            // Do not acknowledge an event that has not been persisted locally.
            if (matches.Count != 1)
            {
                return RetryLater();
            }

            var attemptReference = matches[0];
            var expectedType = attemptReference.PaymentMethod switch
            {
                PaymentMethod.CreditCard => "credit_card",
                PaymentMethod.DebitCard => "debit_card",
                _ => null
            };

            if (expectedType is null)
            {
                return Results.StatusCode(
                    StatusCodes.Status422UnprocessableEntity);
            }

            var command = await verifier.VerifyAsync(
                account.TenantId,
                account.BusinessId,
                attemptReference.Id,
                orderId,
                attemptReference.Amount,
                expectedType,
                cancellationToken);

            // Includes resources that are not yet financially approved.
            // Until that state is classified separately, preserve retries.
            if (command is null)
            {
                return RetryLater();
            }

            var result = await confirmation.ConfirmAsync(
                command, cancellationToken);

            return result.Outcome switch
            {
                ConfirmPaymentOutcome.Confirmed or
                ConfirmPaymentOutcome.ReservationExpired or
                ConfirmPaymentOutcome.AppointmentNotConfirmable or
                ConfirmPaymentOutcome.AdditionalPayment => Results.Ok(),

                ConfirmPaymentOutcome.IdempotencyConflict =>
                    Results.StatusCode(StatusCodes.Status409Conflict),

                ConfirmPaymentOutcome.InvalidNotification =>
                    Results.StatusCode(
                        StatusCodes.Status422UnprocessableEntity),

                _ => RetryLater()
            };
        }
        catch (HttpRequestException)
        {
            return RetryLater();
        }
        catch (OperationCanceledException) when (
            !context.RequestAborted.IsCancellationRequested)
        {
            return RetryLater();
        }
    }

    private static IResult RetryLater() =>
        Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    private static bool TryGetSingle(
        IQueryCollection query, string name, out string value)
    {
        value = string.Empty;
        if (!query.TryGetValue(name, out var values) ||
            values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return false;
        }
        value = values[0]!;
        return true;
    }

    private static bool TryGetSingleHeader(
        HttpContext context, string name, out string value)
    {
        value = string.Empty;
        if (!context.Request.Headers.TryGetValue(name, out var values) ||
            values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return false;
        }
        value = values[0]!;
        return true;
    }
}
