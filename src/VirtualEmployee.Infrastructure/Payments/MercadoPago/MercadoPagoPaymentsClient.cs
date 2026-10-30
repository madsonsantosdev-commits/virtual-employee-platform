using System.Net.Http.Headers;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoPaymentsClient
{
    private readonly HttpClient _httpClient;

    public MercadoPagoPaymentsClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    public async Task<HttpResponseMessage> GetAsync(
        string accessToken,
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAccessToken(accessToken);
        ValidatePaymentId(paymentId);

        var paymentUri = new Uri(
            $"https://api.mercadopago.com/v1/payments/{paymentId}");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            paymentUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

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

    private static void ValidatePaymentId(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId) ||
            paymentId.Length > 200 ||
            paymentId.Any(character =>
                !char.IsAsciiDigit(character)) ||
            paymentId.All(character => character == '0'))
        {
            throw new ArgumentException(
                "Payment ID is invalid.",
                nameof(paymentId));
        }
    }
}