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
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("MercadoPagoWebhookAuthentication")
                .LogWarning(
                    "Webhook rejected before authentication. DataIdCount={DataIdCount}, " +
                    "SignatureCount={SignatureCount}, RequestIdCount={RequestIdCount}",
                    context.Request.Query["data.id"].Count,
                    context.Request.Headers["x-signature"].Count,
                    context.Request.Headers["x-request-id"].Count);
            return Results.Unauthorized();
        }

        var account = authenticator.Authenticate(
            webhookId, signature, requestId, orderId);

        if (account is null)
        {
            LogAuthenticationFailure(context, webhookId, signature, requestId, orderId);
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

    private static void LogAuthenticationFailure(
        HttpContext context, Guid webhookId, string signature,
        string requestId, string dataId)
    {
        var options = context.RequestServices.GetRequiredService<MercadoPagoOptions>();
        var matchingAccounts = options.Accounts
            .Where(account => account is not null && account.WebhookId == webhookId)
            .ToArray();
        var parts = signature.Split(',');
        var timestamps = parts.Where(part =>
            part.Split('=', 2)[0].Trim() == "ts").ToArray();
        var hashes = parts.Where(part =>
            part.Split('=', 2)[0].Trim() == "v1").ToArray();
        var hashLength = hashes.Length == 1 && hashes[0].Contains('=')
            ? hashes[0][(hashes[0].IndexOf('=') + 1)..].Trim().Length
            : -1;
        var identifiersValid = new[] { requestId, dataId }.All(value =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));

        bool? lowercaseMatch = null;
        bool? originalCaseMatch = null;
        if (matchingAccounts.Length == 1 && timestamps.Length == 1 && hashes.Length == 1 &&
            timestamps[0].Contains('=') && hashes[0].Contains('='))
        {
            var timestamp = timestamps[0][(timestamps[0].IndexOf('=') + 1)..].Trim();
            var receivedHash = hashes[0][(hashes[0].IndexOf('=') + 1)..].Trim();
            var secret = matchingAccounts[0].WebhookSecret;
            lowercaseMatch = MatchesDiagnosticHash(
                secret, receivedHash, dataId.ToLowerInvariant(), requestId, timestamp);
            originalCaseMatch = MatchesDiagnosticHash(
                secret, receivedHash, dataId, requestId, timestamp);
        }

        // Log structure only: never credentials, signature hashes, or header values.
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MercadoPagoWebhookAuthentication")
            .LogWarning(
                "Webhook authentication failed. Enabled={Enabled}, " +
                "MatchingAccounts={MatchingAccounts}, SecretLength={SecretLength}, " +
                "SignatureLength={SignatureLength}, Parts={Parts}, " +
                "TimestampParts={TimestampParts}, HashParts={HashParts}, " +
                "HashLength={HashLength}, RequestIdLength={RequestIdLength}, " +
                "DataIdLength={DataIdLength}, IdentifiersValid={IdentifiersValid}, " +
                "LowercaseMatch={LowercaseMatch}, OriginalCaseMatch={OriginalCaseMatch}",
                options.WebhooksEnabled, matchingAccounts.Length,
                matchingAccounts.Length == 1 ? matchingAccounts[0].WebhookSecret?.Length : null,
                signature.Length, parts.Length, timestamps.Length, hashes.Length,
                hashLength, requestId.Length, dataId.Length, identifiersValid,
                lowercaseMatch, originalCaseMatch);
    }

    private static bool MatchesDiagnosticHash(
        string secret, string receivedHash, string dataId,
        string requestId, string timestamp)
    {
        try
        {
            var received = Convert.FromHexString(receivedHash);
            var expected = System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret),
                System.Text.Encoding.UTF8.GetBytes(
                    $"id:{dataId};request-id:{requestId};ts:{timestamp};"));
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                expected, received);
        }
        catch (FormatException)
        {
            return false;
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
