using System.Net;
using System.Text;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoPaymentsClientTests
{
    #region Facts

    [Fact]
    public async Task GetAsync_ShouldSendExpectedRequest()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoPaymentsClient(httpClient);

        using var response = await client.GetAsync(
            "test-token",
            "123456789");

        var request = Assert.Single(handler.Requests);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://api.mercadopago.com/v1/payments/123456789",
            request.Url);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("test-token", request.AuthorizationParameter);
        Assert.Equal("application/json", request.Accept);
        Assert.False(request.HasContent);
        Assert.False(request.HasIdempotencyKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"status":"approved"}""",
            await response.Content.ReadAsStringAsync());

        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
        Assert.False(
            httpClient.DefaultRequestHeaders.Contains(
                "X-Idempotency-Key"));
    }

    [Fact]
    public async Task GetAsync_ShouldKeepTokensOnIndividualRequests()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoPaymentsClient(httpClient);

        using var first = await client.GetAsync(
            "first-business-token",
            "123456789");

        using var second = await client.GetAsync(
            "second-business-token",
            "987654321");

        Assert.Equal(2, handler.Requests.Count);

        Assert.Equal(
            "first-business-token",
            handler.Requests[0].AuthorizationParameter);
        Assert.Equal(
            "second-business-token",
            handler.Requests[1].AuthorizationParameter);

        Assert.Equal(
            "https://api.mercadopago.com/v1/payments/123456789",
            handler.Requests[0].Url);
        Assert.Equal(
            "https://api.mercadopago.com/v1/payments/987654321",
            handler.Requests[1].Url);

        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task GetAsync_WithCancelledToken_ShouldNotSend()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        var client = new MercadoPagoPaymentsClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync(
                "test-token",
                "123456789",
                cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAsync_WithPaymentIdAboveMaximumLength_ShouldNotSend()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoPaymentsClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetAsync(
                "test-token",
                new string('1', 201)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAsync_WithTransportFailure_ShouldPropagateWithoutRetry()
    {
        using var handler = new FailingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoPaymentsClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync(
                "test-token",
                "123456789"));

        Assert.Equal(1, handler.CallCount);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task GetAsync_WithErrorResponse_ShouldReturnWithoutRetry(
        int statusCode)
    {
        using var handler = new RecordingHandler(
            (HttpStatusCode)statusCode);
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoPaymentsClient(httpClient);

        using var response = await client.GetAsync(
            "test-token",
            "123456789");

        Assert.Equal(
            (HttpStatusCode)statusCode,
            response.StatusCode);
        Assert.Equal(
            """{"status":"approved"}""",
            await response.Content.ReadAsStringAsync());

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("token value")]
    [InlineData("token\tvalue")]
    [InlineData("token\nvalue")]
    public async Task GetAsync_WithInvalidToken_ShouldNotSend(
        string? accessToken)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoPaymentsClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetAsync(
                accessToken!,
                "123456789"));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("-123")]
    [InlineData("+123")]
    [InlineData("12.3")]
    [InlineData("1e3")]
    [InlineData("123 456")]
    [InlineData("123\t456")]
    [InlineData("123\n456")]
    [InlineData("ORDTST123")]
    [InlineData("../123")]
    [InlineData("123/456")]
    [InlineData("123?query=value")]
    [InlineData("123#fragment")]
    [InlineData("123%2F456")]
    [InlineData("https://example.com")]
    [InlineData("１２３")]
    public async Task GetAsync_WithInvalidPaymentId_ShouldNotSend(
        string? paymentId)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoPaymentsClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetAsync(
                "test-token",
                paymentId!));

        Assert.Empty(handler.Requests);
    }

    #endregion

    #region Helpers

    private sealed record CapturedRequest(
        HttpMethod Method,
        string? Url,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Accept,
        bool HasContent,
        bool HasIdempotencyKey);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public List<CapturedRequest> Requests { get; } = [];

        public RecordingHandler(
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.AbsoluteUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                Assert.Single(request.Headers.Accept).MediaType
                    ?? string.Empty,
                request.Content is not null,
                request.Headers.Contains("X-Idempotency-Key")));

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    """{"status":"approved"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            throw new HttpRequestException(
                "Simulated transport failure.");
        }
    }

    #endregion
}