using System.Net;
using System.Text;
using System.Text.Json;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoGatewayTests
{
    #region Facts

    [Fact]
    public async Task CreateAsync_WithPix_ShouldRejectWithoutSending()
    {
        var request = CreateRequest() with
        {
            PaymentMethod = PaymentMethod.Pix
        };

        using var handler = UnexpectedRequestHandler();
        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(request, httpClient);

        var result = await gateway.CreateAsync(request);

        Assert.Equal(
            CreateGatewayPaymentOutcome.Rejected,
            result.Outcome);
        Assert.Null(result.Reference);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CreateAsync_WithoutPreparedAt_ShouldNotSend()
    {
        var request = CreateRequest() with
        {
            PreparedAt = null
        };

        using var handler = UnexpectedRequestHandler();
        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(request, httpClient);

        var result = await gateway.CreateAsync(request);

        AssertUnknown(result);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CreateAsync_WithForeignTenant_ShouldNotSend()
    {
        var configuredRequest = CreateRequest();

        var foreignRequest = configuredRequest with
        {
            TenantId = Guid.NewGuid()
        };

        using var handler = UnexpectedRequestHandler();
        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(
            configuredRequest,
            httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.CreateAsync(foreignRequest));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CreateAsync_WithCallerCancellation_ShouldPropagate()
    {
        var request = CreateRequest();

        using var handler = UnexpectedRequestHandler();
        using var httpClient = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        var gateway = CreateGateway(request, httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.CreateAsync(
                request,
                cancellation.Token));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CreateAsync_WithCancellationDuringSend_ShouldPropagate()
    {
        var request = CreateRequest();

        using var cancellation = new CancellationTokenSource();

        using var handler = new StubHandler(
            (_, cancellationToken) =>
            {
                cancellation.Cancel();

                return Task.FromCanceled<HttpResponseMessage>(
                    cancellationToken);
            });

        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(request, httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.CreateAsync(
                request,
                cancellation.Token));

        Assert.Equal(1, handler.Calls);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(PaymentMethod.CreditCard, "credit_card")]
    [InlineData(PaymentMethod.DebitCard, "debit_card")]
    public async Task CreateAsync_ShouldSendOrderAndReadResponse(
        PaymentMethod method,
        string allowedType)
    {
        var request = CreateRequest() with
        {
            PaymentMethod = method
        };

        using var handler = new StubHandler(
            async (message, cancellationToken) =>
            {
                Assert.Equal(
                    "test-token",
                    message.Headers.Authorization?.Parameter);

                Assert.Equal(
                    request.PaymentAttemptId.ToString("D"),
                    Assert.Single(
                        message.Headers.GetValues(
                            "X-Idempotency-Key")));

                var body = await message.Content!
                    .ReadAsStringAsync(cancellationToken);

                using var document = JsonDocument.Parse(body);

                Assert.Equal(
                    request.PaymentAttemptId.ToString("D"),
                    document.RootElement
                        .GetProperty("external_reference")
                        .GetString());

                var excludedTypes = document.RootElement
                    .GetProperty("config")
                    .GetProperty("payment_method")
                    .GetProperty("not_allowed_types")
                    .EnumerateArray()
                    .Select(element => element.GetString()!)
                    .ToArray();

                Assert.DoesNotContain(allowedType, excludedTypes);
                Assert.Contains("prepaid_card", excludedTypes);

                return Response(
                    HttpStatusCode.Created,
                    ValidResponse(request));
            });

        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(request, httpClient);

        var result = await gateway.CreateAsync(request);

        Assert.Equal(
            CreateGatewayPaymentOutcome.Created,
            result.Outcome);

        Assert.NotNull(result.Reference);
        Assert.Equal(
            "MERCADO_PAGO",
            result.Reference.Provider);
        Assert.Equal(
            "ORDTST123",
            result.Reference.ProviderPaymentId);
        Assert.Equal(
            request.ExpiresAt,
            result.Reference.ExpiresAt);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(423)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task CreateAsync_WithHttpError_ShouldReturnUnknown(
        int statusCode)
    {
        var request = CreateRequest();

        using var handler = new StubHandler(
            (_, _) => Task.FromResult(
                Response(
                    (HttpStatusCode)statusCode,
                    """{"error":"simulated"}""")));

        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(request, httpClient);

        var result = await gateway.CreateAsync(request);

        AssertUnknown(result);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("cancelled-by-transport")]
    public async Task CreateAsync_WithTransportFailure_ShouldReturnUnknown(
        string scenario)
    {
        var request = CreateRequest();

        using var handler = new StubHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(
                scenario switch
                {
                    "network" => new HttpRequestException(
                        "Simulated network failure."),

                    "timeout" => new TimeoutException(
                        "Simulated timeout."),

                    "cancelled-by-transport" =>
                        new OperationCanceledException(
                            "Simulated transport cancellation."),

                    _ => throw new ArgumentOutOfRangeException(
                        nameof(scenario))
                }));

        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(request, httpClient);

        var result = await gateway.CreateAsync(request);

        AssertUnknown(result);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("{}")]
    public async Task CreateAsync_WithInvalidSuccessBody_ShouldReturnUnknown(
        string responseBody)
    {
        var request = CreateRequest();

        using var handler = new StubHandler(
            (_, _) => Task.FromResult(
                Response(
                    HttpStatusCode.Created,
                    responseBody)));

        using var httpClient = new HttpClient(handler);

        var gateway = CreateGateway(request, httpClient);

        var result = await gateway.CreateAsync(request);

        AssertUnknown(result);
        Assert.Equal(1, handler.Calls);
    }

    #endregion

    #region Helpers

    private static void AssertUnknown(
        CreateGatewayPaymentResult result)
    {
        Assert.Equal(
            CreateGatewayPaymentOutcome.Unknown,
            result.Outcome);
        Assert.Null(result.Reference);
    }

    private static MercadoPagoGateway CreateGateway(
        CreateGatewayPaymentRequest request,
        HttpClient httpClient)
    {
        var options = new MercadoPagoOptions
        {
            Accounts =
            [
                new MercadoPagoAccountOptions
                {
                    TenantId = request.TenantId,
                    BusinessId = request.BusinessId,
                    AccessToken = "test-token"
                }
            ]
        };

        return new MercadoPagoGateway(
            new MercadoPagoAccountResolver(options),
            new MercadoPagoOrdersClient(httpClient));
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
            PaymentMethod.CreditCard,
            preparedAt.AddMinutes(10))
        {
            PreparedAt = preparedAt
        };
    }

    private static string ValidResponse(
        CreateGatewayPaymentRequest request)
    {
        return JsonSerializer.Serialize(new
        {
            id = "ORDTST123",
            external_reference =
                request.PaymentAttemptId.ToString("D"),
            currency = "BRL",
            total_amount = "65.00",
            checkout_url =
                "https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDTST123",
            expiration_time = "PT10M",
            created_date = request.PreparedAt!.Value
        });
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static StubHandler UnexpectedRequestHandler()
    {
        return new StubHandler(
            (_, _) => throw new InvalidOperationException(
                "No HTTP request was expected."));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _send;

        public int Calls { get; private set; }

        public StubHandler(
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;

            return _send(request, cancellationToken);
        }
    }

    #endregion
}