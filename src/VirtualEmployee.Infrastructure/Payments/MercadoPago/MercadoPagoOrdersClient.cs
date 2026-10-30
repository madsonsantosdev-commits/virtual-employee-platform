using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoOrdersClient
{
    private static readonly Uri CreateOrderUri =
        new("https://api.mercadopago.com/v1/orders");

    private readonly HttpClient _httpClient;

    public MercadoPagoOrdersClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    public async Task<HttpResponseMessage> CreateAsync(
        string accessToken,
        Guid paymentAttemptId,
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        ValidateAccessToken(accessToken);

        if (paymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "PaymentAttemptId must not be empty.",
                nameof(paymentAttemptId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);

        using (var document = JsonDocument.Parse(requestJson))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Order request must be a JSON object.",
                    nameof(requestJson));
            }
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateOrderUri);

        ConfigureHeaders(request, accessToken);

        request.Headers.Add(
            "X-Idempotency-Key",
            paymentAttemptId.ToString("D"));

        request.Content = new StringContent(
            requestJson,
            Encoding.UTF8,
            "application/json");

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
    }

    public async Task<HttpResponseMessage> GetAsync(
        string accessToken,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        ValidateAccessToken(accessToken);
        ValidateOrderId(orderId);

        var orderUri = new Uri(
            $"{CreateOrderUri.AbsoluteUri}/{Uri.EscapeDataString(orderId)}");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            orderUri);

        ConfigureHeaders(request, accessToken);

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
    }

    private static void ValidateAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) ||
            accessToken.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            throw new ArgumentException(
                "Access token is invalid.",
                nameof(accessToken));
        }
    }

    private static void ValidateOrderId(string orderId)
    {
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
    }

    private static void ConfigureHeaders(
        HttpRequestMessage request,
        string accessToken)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
    }
}