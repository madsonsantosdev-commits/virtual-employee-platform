using System.Globalization;
using System.Text.Json;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public static class MercadoPagoExpiredOrderReader
{
    // Only authenticated GET /v1/orders responses may be passed here.
    // Expired orders can omit total_paid_amount and transactions entirely.
    public static bool IsExpired(
        string json, string orderId, long collectorId, long applicationId,
        Guid attemptId, decimal amount)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!IsUnambiguous(root) ||
                !EqualsText(root, "id", orderId) ||
                !EqualsText(root, "type", "online") ||
                !EqualsText(root, "status", "canceled") ||
                !EqualsText(root, "status_detail", "expired") ||
                !EqualsText(root, "currency", "BRL") ||
                !EqualsIdentifier(root, "user_id", collectorId) ||
                !EqualsText(root, "external_reference", attemptId.ToString("D")) ||
                !EqualsAmount(root, "total_amount", amount) ||
                !root.TryGetProperty("integration_data", out var integration) ||
                !EqualsIdentifier(integration, "application_id", applicationId))
            {
                return false;
            }

            // Any evidence of settlement needs financial reconciliation instead.
            if (root.TryGetProperty("total_paid_amount", out _) &&
                !EqualsAmount(root, "total_paid_amount", 0m))
            {
                return false;
            }

            if (root.TryGetProperty("transactions", out var transactions))
            {
                if (transactions.ValueKind != JsonValueKind.Object)
                    return false;

                // Keep orders with payment transactions retryable until classified.
                if (transactions.EnumerateObject().Any(property =>
                    property.Name != "payments" ||
                    property.Value.ValueKind != JsonValueKind.Array ||
                    property.Value.GetArrayLength() != 0))
                    return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool EqualsText(JsonElement root, string name, string expected) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool EqualsIdentifier(JsonElement root, string name, long expected)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() == expected.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Number => value.TryGetInt64(out var number) && number == expected,
            _ => false
        };
    }

    private static bool EqualsAmount(JsonElement root, string name, decimal expected)
    {
        if (!root.TryGetProperty(name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out var number) && number == expected,
            JsonValueKind.String => decimal.TryParse(value.GetString(),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && number == expected,
            _ => false
        };
    }

    private static bool IsUnambiguous(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || !IsUnambiguous(property.Value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (!IsUnambiguous(item)) return false;
        }
        return true;
    }
}
