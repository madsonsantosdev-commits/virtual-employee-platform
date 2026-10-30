using System.Text.Json;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoOrderResponseReaderTests
{
    [Fact]
    public void Read_WithValidResponse_ShouldReturnExternalReference()
    {
        var request = CreateRequest();

        var result = MercadoPagoOrderResponseReader.Read(
            CreateResponse(request),
            request);

        Assert.Equal(
            CreateGatewayPaymentOutcome.Created,
            result.Outcome);

        var reference = result.Reference;

        Assert.NotNull(reference);
        Assert.Equal("MERCADO_PAGO", reference.Provider);
        Assert.Equal("ORDTST123", reference.ProviderPaymentId);
        Assert.Equal(
            "https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDTST123",
            reference.CheckoutUrl);
        Assert.Equal(request.ExpiresAt, reference.ExpiresAt);
    }

    [Theory]
    [InlineData(60, 600)]
    [InlineData(-60, 540)]
    public void Read_ShouldUseEarlierExpiration(
        int creationOffsetSeconds,
        int expectedValiditySeconds)
    {
        var request = CreateRequest();

        var createdAt = request.PreparedAt!.Value
            .AddSeconds(creationOffsetSeconds);

        var result = MercadoPagoOrderResponseReader.Read(
            CreateResponse(
                request,
                createdAt: createdAt),
            request);

        Assert.Equal(
            CreateGatewayPaymentOutcome.Created,
            result.Outcome);

        Assert.NotNull(result.Reference);
        Assert.Equal(
            request.PreparedAt.Value.AddSeconds(
                expectedValiditySeconds),
            result.Reference.ExpiresAt);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("reference")]
    public void Read_WithMismatchedPaymentData_ShouldReturnUnknown(
        string field)
    {
        var request = CreateRequest();

        var response = CreateResponse(
            request,
            amount: field == "amount" ? "66.00" : "65.00",
            currency: field == "currency" ? "USD" : "BRL",
            externalReference: field == "reference"
                ? Guid.NewGuid().ToString("D")
                : request.PaymentAttemptId.ToString("D"));

        AssertUnknown(response, request);
    }

    [Theory]
    [InlineData("http://www.mercadopago.com.br/checkout/v1/redirect")]
    [InlineData("https://example.com/checkout/v1/redirect")]
    [InlineData("https://www.mercadopago.com.br.example.com/checkout/")]
    [InlineData("https://user:password@www.mercadopago.com.br/checkout/")]
    [InlineData("https://www.mercadopago.com.br:444/checkout/")]
    [InlineData("https://www.mercadopago.com.br/another-path")]
    [InlineData("invalid-url")]
    public void Read_WithInvalidCheckoutUrl_ShouldReturnUnknown(
        string checkoutUrl)
    {
        var request = CreateRequest();

        AssertUnknown(
            CreateResponse(
                request,
                checkoutUrl: checkoutUrl),
            request);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid-json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Read_WithInvalidResponse_ShouldReturnUnknown(
        string responseJson)
    {
        AssertUnknown(responseJson, CreateRequest());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("order with spaces")]
    [InlineData("order\tvalue")]
    public void Read_WithInvalidOrderId_ShouldReturnUnknown(
        string orderId)
    {
        var request = CreateRequest();

        AssertUnknown(
            CreateResponse(
                request,
                orderId: orderId),
            request);
    }

    [Theory]
    [InlineData("invalid-duration")]
    [InlineData("PT0S")]
    [InlineData("-PT1M")]
    public void Read_WithInvalidExpiration_ShouldReturnUnknown(
        string expirationTime)
    {
        var request = CreateRequest();

        AssertUnknown(
            CreateResponse(
                request,
                expirationTime: expirationTime),
            request);
    }

    [Fact]
    public void Read_WithMissingCreationDate_ShouldReturnUnknown()
    {
        var request = CreateRequest();

        var response = JsonSerializer.Serialize(new
        {
            id = "ORDTST123",
            external_reference =
                request.PaymentAttemptId.ToString("D"),
            currency = "BRL",
            total_amount = "65.00",
            checkout_url =
                "https://www.mercadopago.com.br/checkout/v1/redirect",
            expiration_time = "PT10M"
        });

        AssertUnknown(response, request);
    }

    private static void AssertUnknown(
        string responseJson,
        CreateGatewayPaymentRequest request)
    {
        var result = MercadoPagoOrderResponseReader.Read(
            responseJson,
            request);

        Assert.Equal(
            CreateGatewayPaymentOutcome.Unknown,
            result.Outcome);

        Assert.Null(result.Reference);
    }

    private static CreateGatewayPaymentRequest CreateRequest()
    {
        var preparedAt = new DateTimeOffset(
            2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        return new CreateGatewayPaymentRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            65m,
            PaymentMethod.Pix,
            preparedAt.AddMinutes(10))
        {
            PreparedAt = preparedAt
        };
    }

    private static string CreateResponse(
        CreateGatewayPaymentRequest request,
        string orderId = "ORDTST123",
        string amount = "65.00",
        string currency = "BRL",
        string? externalReference = null,
        string checkoutUrl =
            "https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDTST123",
        string expirationTime = "PT10M",
        DateTimeOffset? createdAt = null)
    {
        return JsonSerializer.Serialize(new
        {
            id = orderId,
            external_reference = externalReference
                ?? request.PaymentAttemptId.ToString("D"),
            currency,
            total_amount = amount,
            checkout_url = checkoutUrl,
            expiration_time = expirationTime,
            created_date = createdAt ?? request.PreparedAt!.Value
        });
    }
}