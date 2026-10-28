using System.Globalization;
using System.Text.Json;
using System.Xml;
using VirtualEmployee.Application.Payments.Gateway;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public static class MercadoPagoOrderResponseReader
{
    public static CreateGatewayPaymentResult Read(
        string responseJson,
        CreateGatewayPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return CreateGatewayPaymentResult.Unknown();
        }

        try
        {
            using var document = JsonDocument.Parse(responseJson);

            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return CreateGatewayPaymentResult.Unknown();
            }

            var orderId = GetString(root, "id");
            var externalReference = GetString(
                root,
                "external_reference");
            var currency = GetString(root, "currency");
            var amountText = GetString(root, "total_amount");
            var checkoutUrl = GetString(root, "checkout_url");
            var expirationText = GetString(
                root,
                "expiration_time");

            if (!IsValidOrderId(orderId) ||
                !string.Equals(
                    externalReference,
                    request.PaymentAttemptId.ToString("D"),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    currency,
                    "BRL",
                    StringComparison.Ordinal) ||
                !decimal.TryParse(
                    amountText,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var amount) ||
                amount != request.Amount ||
                !IsValidCheckoutUrl(checkoutUrl))
            {
                return CreateGatewayPaymentResult.Unknown();
            }

            if (!root.TryGetProperty(
                    "created_date",
                    out var createdDateElement) ||
                createdDateElement.ValueKind != JsonValueKind.String ||
                !createdDateElement.TryGetDateTimeOffset(
                    out var createdAt) ||
                string.IsNullOrWhiteSpace(expirationText))
            {
                return CreateGatewayPaymentResult.Unknown();
            }

            var validity = XmlConvert.ToTimeSpan(expirationText);

            if (validity <= TimeSpan.Zero)
            {
                return CreateGatewayPaymentResult.Unknown();
            }

            var providerExpiresAt = createdAt
                .ToUniversalTime()
                .Add(validity);

            var effectiveExpiresAt =
                providerExpiresAt < request.ExpiresAt
                    ? providerExpiresAt
                    : request.ExpiresAt.ToUniversalTime();

            return CreateGatewayPaymentResult.Created(
                new GatewayPaymentReference(
                    "MERCADO_PAGO",
                    orderId!,
                    checkoutUrl,
                    effectiveExpiresAt));
        }
        catch (JsonException)
        {
            return CreateGatewayPaymentResult.Unknown();
        }
        catch (FormatException)
        {
            return CreateGatewayPaymentResult.Unknown();
        }
        catch (OverflowException)
        {
            return CreateGatewayPaymentResult.Unknown();
        }
        catch (ArgumentOutOfRangeException)
        {
            return CreateGatewayPaymentResult.Unknown();
        }
    }

    private static string? GetString(
        JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(
                propertyName,
                out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool IsValidOrderId(string? orderId)
    {
        return !string.IsNullOrWhiteSpace(orderId) &&
            orderId.Length <= 200 &&
            !orderId.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character));
    }

    private static bool IsValidCheckoutUrl(string? checkoutUrl)
    {
        return Uri.TryCreate(
                checkoutUrl,
                UriKind.Absolute,
                out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(
                uri.Host,
                "www.mercadopago.com.br",
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            uri.AbsolutePath.StartsWith(
                "/checkout/",
                StringComparison.Ordinal);
    }
}