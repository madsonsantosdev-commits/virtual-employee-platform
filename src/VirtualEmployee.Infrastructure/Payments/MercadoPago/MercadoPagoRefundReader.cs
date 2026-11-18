using System.Globalization;
using System.Text.Json;
using VirtualEmployee.Domain.Payments;
using static VirtualEmployee.Infrastructure.Payments.MercadoPago.MercadoPagoProcessedOrderReader;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public static class MercadoPagoRefundReader
{
    public static string? ReadCompleted(
        string json, string orderId, decimal amount, Guid? paymentAttemptId = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var order = document.RootElement;
            if (!IsUnambiguousObject(order) ||
                !HasString(order, "id", orderId) ||
                !HasString(order, "status_detail", "refunded") ||
                (paymentAttemptId is { } attemptId &&
                    !HasString(order, "external_reference", attemptId.ToString("D"))) ||
                !order.TryGetProperty("transactions", out var transactions) ||
                !IsUnambiguousObject(transactions) ||
                !transactions.TryGetProperty("refunds", out var refunds) ||
                refunds.ValueKind != JsonValueKind.Array ||
                refunds.GetArrayLength() != 1)
            {
                return null;
            }

            var refund = refunds[0];
            if (!IsUnambiguousObject(refund) ||
                !HasString(refund, "status", "processed") ||
                !refund.TryGetProperty("amount", out var total) ||
                !MatchesAmount(total, amount) ||
                !refund.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var reference = id.GetString();
            return !string.IsNullOrWhiteSpace(reference) &&
                reference.Length <= PaymentAttempt.ProviderPaymentIdMaxLength &&
                reference.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character))
                    ? reference
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool MatchesAmount(JsonElement value, decimal expected) => value.ValueKind switch
    {
        JsonValueKind.String => decimal.TryParse(
            value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && amount == expected,
        JsonValueKind.Number => value.TryGetDecimal(out var amount) && amount == expected,
        _ => false
    };

    private static bool HasString(JsonElement element, string name, string value) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        property.ValueEquals(value);
}
