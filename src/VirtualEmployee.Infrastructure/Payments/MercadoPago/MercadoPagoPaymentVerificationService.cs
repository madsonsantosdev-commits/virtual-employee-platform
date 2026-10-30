using System.Net;
using VirtualEmployee.Application.Payments.ConfirmPayment;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoPaymentVerificationService
{
    private readonly MercadoPagoAccountResolver _accountResolver;
    private readonly MercadoPagoOrdersClient _ordersClient;
    private readonly MercadoPagoPaymentsClient _paymentsClient;

    public MercadoPagoPaymentVerificationService(
        MercadoPagoAccountResolver accountResolver,
        MercadoPagoOrdersClient ordersClient,
        MercadoPagoPaymentsClient paymentsClient)
    {
        ArgumentNullException.ThrowIfNull(accountResolver);
        ArgumentNullException.ThrowIfNull(ordersClient);
        ArgumentNullException.ThrowIfNull(paymentsClient);

        _accountResolver = accountResolver;
        _ordersClient = ordersClient;
        _paymentsClient = paymentsClient;
    }

    public async Task<ConfirmPaymentCommand?> VerifyAsync(
        Guid tenantId,
        Guid businessId,
        Guid paymentAttemptId,
        string orderId,
        decimal expectedAmount,
        string expectedPaymentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateExpectedValues(
            paymentAttemptId,
            orderId,
            expectedAmount,
            expectedPaymentType);

        var account = _accountResolver.ResolveForConfirmation(
            tenantId,
            businessId);

        using var orderResponse = await _ordersClient.GetAsync(
            account.AccessToken,
            orderId,
            cancellationToken);

        RequireOk(orderResponse);

        var orderJson = await orderResponse.Content.ReadAsStringAsync(
            cancellationToken);

        var order = MercadoPagoProcessedOrderReader.Read(
            orderJson,
            orderId,
            account.CollectorId,
            account.ApplicationId,
            paymentAttemptId,
            expectedAmount);

        if (order is null ||
            !string.Equals(
                order.PaymentType,
                expectedPaymentType,
                StringComparison.Ordinal))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var paymentResponse = await _paymentsClient.GetAsync(
            account.AccessToken,
            order.PaymentId,
            cancellationToken);

        RequireOk(paymentResponse);

        var paymentJson = await paymentResponse.Content.ReadAsStringAsync(
            cancellationToken);

        var payment = MercadoPagoPaymentResponseReader.Read(
            paymentJson,
            order.PaymentId,
            account.CollectorId,
            paymentAttemptId,
            expectedAmount);

        if (payment is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new ConfirmPaymentCommand(
            paymentAttemptId,
            "MERCADO_PAGO",
            order.OrderId,
            payment.PaymentId,
            payment.Amount,
            payment.Currency,
            payment.ApprovedAt);
    }

    private static void ValidateExpectedValues(
        Guid paymentAttemptId,
        string orderId,
        decimal expectedAmount,
        string expectedPaymentType)
    {
        if (paymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "PaymentAttemptId must not be empty.",
                nameof(paymentAttemptId));
        }

        if (string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 200 ||
            orderId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character != '-'))
        {
            throw new ArgumentException(
                "Order ID is invalid.",
                nameof(orderId));
        }

        if (expectedAmount <= 0m ||
            expectedAmount > 9999999999.99m ||
            decimal.Round(expectedAmount, 2) != expectedAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedAmount));
        }

        if (expectedPaymentType is not ("credit_card" or "debit_card"))
        {
            throw new ArgumentException(
                "Expected payment type is invalid.",
                nameof(expectedPaymentType));
        }
    }

    private static void RequireOk(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                "Mercado Pago resource query did not return HTTP 200.",
                null,
                response.StatusCode);
        }
    }
}