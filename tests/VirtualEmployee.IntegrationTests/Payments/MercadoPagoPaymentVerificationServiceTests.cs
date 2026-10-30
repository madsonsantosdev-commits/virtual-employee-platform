using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoPaymentVerificationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid AttemptId = Guid.NewGuid();

    private const string OrderId = "ORDTST123";
    private const string PaymentId = "123456";
    private const long CollectorId = 987654321;
    private const long ApplicationId = 123456789;

    #region Facts

    [Fact]
    public async Task VerifyAsync_WithValidResources_ShouldReturnConfirmation()
    {
        using var fixture = new TestFixture();

        var result = await fixture.VerifyAsync();

        Assert.NotNull(result);
        Assert.Equal(AttemptId, result.PaymentAttemptId);
        Assert.Equal("MERCADO_PAGO", result.Provider);
        Assert.Equal(OrderId, result.ProviderOrderId);
        Assert.Equal(PaymentId, result.ProviderPaymentId);
        Assert.Equal(65m, result.Amount);
        Assert.Equal("BRL", result.Currency);
        Assert.Equal(
            new DateTimeOffset(
                2026, 10, 28, 20, 38, 17, TimeSpan.Zero),
            result.ApprovedAt);
        Assert.Equal(TimeSpan.Zero, result.ApprovedAt.Offset);

        Assert.Equal(
            new[]
            {
                $"/v1/orders/{OrderId}",
                $"/v1/payments/{PaymentId}"
            },
            fixture.Handler.Paths);

        Assert.All(
            fixture.Handler.Tokens,
            token => Assert.Equal("business-token", token));

        Assert.Null(
            fixture.HttpClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task VerifyAsync_WithCancelledToken_ShouldNotSend()
    {
        using var fixture = new TestFixture();
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.VerifyAsync(
                TenantId,
                BusinessId,
                AttemptId,
                OrderId,
                65m,
                "credit_card",
                cancellation.Token));

        Assert.Empty(fixture.Handler.Paths);
    }

    [Fact]
    public async Task VerifyAsync_WithTransportFailure_ShouldPropagateWithoutRetry()
    {
        using var fixture = new TestFixture();

        fixture.Handler.FailTransport = true;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            fixture.VerifyAsync());

        Assert.Single(fixture.Handler.Paths);
    }

    [Fact]
    public async Task VerifyAsync_WithDifferentExpectedPaymentType_ShouldNotQueryPayment()
    {
        using var fixture = new TestFixture();

        var result = await fixture.Service.VerifyAsync(
            TenantId,
            BusinessId,
            AttemptId,
            OrderId,
            65m,
            "debit_card");

        Assert.Null(result);
        Assert.Single(fixture.Handler.Paths);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("order", 401)]
    [InlineData("order", 404)]
    [InlineData("order", 429)]
    [InlineData("order", 500)]
    [InlineData("order", 201)]
    [InlineData("payment", 401)]
    [InlineData("payment", 404)]
    [InlineData("payment", 429)]
    [InlineData("payment", 500)]
    [InlineData("payment", 201)]
    public async Task VerifyAsync_WithUnexpectedHttpStatus_ShouldPropagateWithoutRetry(
        string resource,
        int statusCode)
    {
        using var fixture = new TestFixture();

        if (resource == "order")
        {
            fixture.Handler.OrderStatus = (HttpStatusCode)statusCode;
        }
        else
        {
            fixture.Handler.PaymentStatus = (HttpStatusCode)statusCode;
        }

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            fixture.VerifyAsync());

        Assert.Equal(
            (HttpStatusCode)statusCode,
            exception.StatusCode);

        Assert.Equal(
            resource == "order" ? 1 : 2,
            fixture.Handler.Paths.Count);
    }

    [Theory]
    [InlineData("order", "status", "\"created\"")]
    [InlineData("order", "user_id", "\"111111111\"")]
    [InlineData("order", "external_reference", "\"invalid\"")]
    [InlineData("payment", "status", "\"pending\"")]
    [InlineData("payment", "collector_id", "111111111")]
    [InlineData("payment", "transaction_amount", "66")]
    public async Task VerifyAsync_WithIncompatibleResource_ShouldReturnNull(
        string resource,
        string property,
        string valueJson)
    {
        using var fixture = new TestFixture();

        var root = JsonNode.Parse(
            resource == "order"
                ? fixture.Handler.OrderJson
                : fixture.Handler.PaymentJson)!.AsObject();

        root[property] = JsonNode.Parse(valueJson);

        if (resource == "order")
        {
            fixture.Handler.OrderJson = root.ToJsonString();
        }
        else
        {
            fixture.Handler.PaymentJson = root.ToJsonString();
        }

        Assert.Null(await fixture.VerifyAsync());

        Assert.Equal(
            resource == "order" ? 1 : 2,
            fixture.Handler.Paths.Count);
    }

    [Theory]
    [InlineData("collector")]
    [InlineData("application")]
    public async Task VerifyAsync_WithInvalidAccountIdentifiers_ShouldNotSend(
        string identifier)
    {
        using var fixture = new TestFixture();

        if (identifier == "collector")
        {
            fixture.Account.CollectorId = 0;
        }
        else
        {
            fixture.Account.ApplicationId = 0;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.VerifyAsync());

        Assert.Empty(fixture.Handler.Paths);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("business")]
    public async Task VerifyAsync_WithAnotherAccountScope_ShouldNotSend(
        string scope)
    {
        using var fixture = new TestFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.VerifyAsync(
                scope == "tenant" ? Guid.NewGuid() : TenantId,
                scope == "business" ? Guid.NewGuid() : BusinessId,
                AttemptId,
                OrderId,
                65m,
                "credit_card"));

        Assert.Empty(fixture.Handler.Paths);
    }

    #endregion

    #region Helpers

    private sealed class TestFixture : IDisposable
    {
        public MercadoPagoAccountOptions Account { get; }

        public RecordingHandler Handler { get; }

        public HttpClient HttpClient { get; }

        public MercadoPagoPaymentVerificationService Service { get; }

        public TestFixture()
        {
            Account = new MercadoPagoAccountOptions
            {
                TenantId = TenantId,
                BusinessId = BusinessId,
                AccessToken = "business-token",
                CollectorId = CollectorId,
                ApplicationId = ApplicationId
            };

            var resolver = new MercadoPagoAccountResolver(
                new MercadoPagoOptions
                {
                    Accounts = [Account]
                });

            Handler = new RecordingHandler();
            HttpClient = new HttpClient(Handler);

            Service = new MercadoPagoPaymentVerificationService(
                resolver,
                new MercadoPagoOrdersClient(HttpClient),
                new MercadoPagoPaymentsClient(HttpClient));
        }

        public Task<VirtualEmployee.Application.Payments.ConfirmPayment
            .ConfirmPaymentCommand?> VerifyAsync()
        {
            return Service.VerifyAsync(
                TenantId,
                BusinessId,
                AttemptId,
                OrderId,
                65m,
                "credit_card");
        }

        public void Dispose()
        {
            HttpClient.Dispose();
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public List<string?> Tokens { get; } = [];

        public HttpStatusCode OrderStatus { get; set; } =
            HttpStatusCode.OK;

        public HttpStatusCode PaymentStatus { get; set; } =
            HttpStatusCode.OK;

        public bool FailTransport { get; set; }

        public string OrderJson { get; set; } = CreateOrderJson();

        public string PaymentJson { get; set; } = CreatePaymentJson();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https", request.RequestUri?.Scheme);
            Assert.Equal(
                "api.mercadopago.com",
                request.RequestUri?.Host);
            Assert.Equal(
                "Bearer",
                request.Headers.Authorization?.Scheme);
            Assert.Null(request.Content);
            Assert.False(
                request.Headers.Contains("X-Idempotency-Key"));

            var path = request.RequestUri!.AbsolutePath;

            Paths.Add(path);
            Tokens.Add(request.Headers.Authorization?.Parameter);

            if (FailTransport)
            {
                throw new HttpRequestException(
                    "Simulated transport failure.");
            }

            var isOrder = path == $"/v1/orders/{OrderId}";

            if (!isOrder)
            {
                Assert.Equal($"/v1/payments/{PaymentId}", path);
            }

            return Task.FromResult(new HttpResponseMessage(
                isOrder ? OrderStatus : PaymentStatus)
            {
                Content = new StringContent(
                    isOrder ? OrderJson : PaymentJson,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private static string CreateOrderJson()
    {
        return new JsonObject
        {
            ["id"] = OrderId,
            ["type"] = "online",
            ["status"] = "processed",
            ["status_detail"] = "accredited",
            ["external_reference"] = AttemptId.ToString("D"),
            ["user_id"] = "987654321",
            ["total_amount"] = "65.00",
            ["total_paid_amount"] = "65.00",
            ["currency"] = "BRL",
            ["integration_data"] = new JsonObject
            {
                ["application_id"] = "123456789"
            },
            ["transactions"] = new JsonObject
            {
                ["payments"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "PAY123",
                        ["reference_id"] = PaymentId,
                        ["status"] = "processed",
                        ["status_detail"] = "accredited",
                        ["amount"] = "65",
                        ["paid_amount"] = "65",
                        ["payment_method"] = new JsonObject
                        {
                            ["type"] = "credit_card"
                        }
                    }
                }
            }
        }.ToJsonString();
    }

    private static string CreatePaymentJson()
    {
        return new JsonObject
        {
            ["id"] = 123456L,
            ["collector_id"] = CollectorId,
            ["status"] = "approved",
            ["status_detail"] = "accredited",
            ["transaction_amount"] = 65m,
            ["transaction_amount_refunded"] = 0m,
            ["currency_id"] = "BRL",
            ["external_reference"] = AttemptId.ToString("D"),
            ["date_approved"] = "2026-10-28T17:38:17-03:00"
        }.ToJsonString();
    }

    #endregion
}