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
        if (string.IsNullOrWhiteSpace(accessToken) ||
            accessToken.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            throw new ArgumentException(
                "Access token is invalid.",
                nameof(accessToken));
        }

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

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

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
}