using System.Net;
using System.Text;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoOrdersClientTests
{
    [Fact]
    public async Task CreateAsync_ShouldSendExpectedRequest()
    {
        var attemptId = Guid.NewGuid();
        const string body = """{"total_amount":"65.00"}""";

        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoOrdersClient(httpClient);

        using var response = await client.CreateAsync(
            "test-token",
            attemptId,
            body);

        var request = Assert.Single(handler.Requests);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://api.mercadopago.com/v1/orders",
            request.Url);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("test-token", request.AuthorizationParameter);
        Assert.Equal(
            attemptId.ToString("D"),
            request.IdempotencyKey);
        Assert.Equal("application/json", request.Accept);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("utf-8", request.Charset);
        Assert.Equal(body, request.Body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
        Assert.False(
            httpClient.DefaultRequestHeaders.Contains(
                "X-Idempotency-Key"));
    }

    [Fact]
    public async Task CreateAsync_WithSameAttempt_ShouldPreserveKeyAndBody()
    {
        var attemptId = Guid.NewGuid();
        const string body = """{"total_amount":"65.00"}""";

        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoOrdersClient(httpClient);

        using var first = await client.CreateAsync(
            "test-token",
            attemptId,
            body);

        using var second = await client.CreateAsync(
            "test-token",
            attemptId,
            body);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            handler.Requests[0].IdempotencyKey,
            handler.Requests[1].IdempotencyKey);
        Assert.Equal(
            handler.Requests[0].Body,
            handler.Requests[1].Body);
    }

    [Fact]
    public async Task CreateAsync_ShouldKeepTokensOnIndividualRequests()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoOrdersClient(httpClient);

        using var first = await client.CreateAsync(
            "first-business-token",
            Guid.NewGuid(),
            "{}");

        using var second = await client.CreateAsync(
            "second-business-token",
            Guid.NewGuid(),
            "{}");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            "first-business-token",
            handler.Requests[0].AuthorizationParameter);
        Assert.Equal(
            "second-business-token",
            handler.Requests[1].AuthorizationParameter);
        Assert.NotEqual(
            handler.Requests[0].IdempotencyKey,
            handler.Requests[1].IdempotencyKey);

        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task CreateAsync_WithErrorResponse_ShouldReturnWithoutRetry(
        int statusCode)
    {
        using var handler = new RecordingHandler(
            (HttpStatusCode)statusCode);

        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoOrdersClient(httpClient);

        using var response = await client.CreateAsync(
            "test-token",
            Guid.NewGuid(),
            "{}");

        Assert.Equal(
            (HttpStatusCode)statusCode,
            response.StatusCode);

        Assert.Equal(
            """{"result":"simulated"}""",
            await response.Content.ReadAsStringAsync());

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("token value")]
    [InlineData("token\tvalue")]
    [InlineData("token\nvalue")]
    public async Task CreateAsync_WithInvalidToken_ShouldNotSend(
        string accessToken)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoOrdersClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateAsync(
                accessToken,
                Guid.NewGuid(),
                "{}"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyAttemptId_ShouldNotSend()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoOrdersClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateAsync(
                "test-token",
                Guid.Empty,
                "{}"));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("123")]
    public async Task CreateAsync_WithNonObjectBody_ShouldNotSend(
        string body)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);

        var client = new MercadoPagoOrdersClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateAsync(
                "test-token",
                Guid.NewGuid(),
                body));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateAsync_WithCancelledToken_ShouldNotSend()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        var client = new MercadoPagoOrdersClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CreateAsync(
                "test-token",
                Guid.NewGuid(),
                "{}",
                cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string? Url,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string IdempotencyKey,
        string Accept,
        string? ContentType,
        string? Charset,
        string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public List<CapturedRequest> Requests { get; } = [];

        public RecordingHandler(
            HttpStatusCode statusCode = HttpStatusCode.Created)
        {
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = request.Content
                ?? throw new InvalidOperationException(
                    "Request content is required.");

            var body = await content.ReadAsStringAsync(
                cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.AbsoluteUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                Assert.Single(
                    request.Headers.GetValues(
                        "X-Idempotency-Key")),
                Assert.Single(request.Headers.Accept).MediaType
                    ?? string.Empty,
                content.Headers.ContentType?.MediaType,
                content.Headers.ContentType?.CharSet,
                body));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    """{"result":"simulated"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}