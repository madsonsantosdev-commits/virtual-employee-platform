using System.Globalization;
using System.Text.Json;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public static class MercadoPagoProcessedOrderReader
{
    public static MercadoPagoProcessedOrder? Read(
        string? responseJson,
        string expectedOrderId,
        long expectedCollectorId,
        long expectedApplicationId,
        Guid expectedPaymentAttemptId,
        decimal expectedAmount)
    {
        ValidateExpectedValues(
            expectedOrderId,
            expectedCollectorId,
            expectedApplicationId,
            expectedPaymentAttemptId,
            expectedAmount);

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseJson);

            var root = document.RootElement;

            if (!IsUnambiguousObject(root) ||
                !HasStringValue(root, "id", expectedOrderId) ||
                !HasStringValue(root, "type", "online") ||
                !HasStringValue(root, "status", "processed") ||
                !HasStringValue(root, "status_detail", "accredited") ||
                !HasStringValue(root, "currency", "BRL"))
            {
                return null;
            }

            if (!HasStringOrNumberValue(
                    root,
                    "user_id",
                    expectedCollectorId.ToString(
                        CultureInfo.InvariantCulture)))
            {
                return null;
            }

            if (!root.TryGetProperty(
                    "integration_data",
                    out var integrationData) ||
                !IsUnambiguousObject(integrationData) ||
                !HasStringOrNumberValue(
                    integrationData,
                    "application_id",
                    expectedApplicationId.ToString(
                        CultureInfo.InvariantCulture)))
            {
                return null;
            }

            if (!root.TryGetProperty(
                    "external_reference",
                    out var externalReference) ||
                externalReference.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(
                    externalReference.GetString(),
                    "D",
                    out var attemptId) ||
                attemptId != expectedPaymentAttemptId)
            {
                return null;
            }

            if (!HasAmount(root, "total_amount", expectedAmount) ||
                !HasAmount(root, "total_paid_amount", expectedAmount))
            {
                return null;
            }

            if (!root.TryGetProperty(
                    "transactions",
                    out var transactions) ||
                !IsUnambiguousObject(transactions) ||
                !transactions.TryGetProperty(
                    "payments",
                    out var payments) ||
                payments.ValueKind != JsonValueKind.Array ||
                payments.GetArrayLength() != 1)
            {
                return null;
            }

            var payment = payments[0];

            if (!IsUnambiguousObject(payment) ||
                !HasStringValue(payment, "status", "processed") ||
                !HasStringValue(
                    payment,
                    "status_detail",
                    "accredited") ||
                !HasAmount(payment, "amount", expectedAmount) ||
                !HasAmount(payment, "paid_amount", expectedAmount))
            {
                return null;
            }

            if (!payment.TryGetProperty(
                    "reference_id",
                    out var referenceId) ||
                !IsPositiveIdentifier(referenceId))
            {
                return null;
            }

            if (!payment.TryGetProperty(
                    "payment_method",
                    out var paymentMethod) ||
                !IsUnambiguousObject(paymentMethod) ||
                !paymentMethod.TryGetProperty(
                    "type",
                    out var paymentTypeProperty) ||
                paymentTypeProperty.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var paymentType = paymentTypeProperty.GetString();

            if (paymentType is not ("credit_card" or "debit_card"))
            {
                return null;
            }

            return new MercadoPagoProcessedOrder(
                expectedOrderId,
                GetIdentifierText(referenceId),
                attemptId,
                expectedCollectorId,
                expectedApplicationId,
                expectedAmount,
                "BRL",
                paymentType!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateExpectedValues(
        string expectedOrderId,
        long expectedCollectorId,
        long expectedApplicationId,
        Guid expectedPaymentAttemptId,
        decimal expectedAmount)
    {
        if (string.IsNullOrWhiteSpace(expectedOrderId) ||
            expectedOrderId.Length > 200 ||
            expectedOrderId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character != '-'))
        {
            throw new ArgumentException(
                "Expected order ID is invalid.",
                nameof(expectedOrderId));
        }

        if (expectedCollectorId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedCollectorId));
        }

        if (expectedApplicationId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedApplicationId));
        }

        if (expectedPaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "Expected PaymentAttemptId must not be empty.",
                nameof(expectedPaymentAttemptId));
        }

        if (expectedAmount <= 0m ||
            expectedAmount > 9999999999.99m ||
            decimal.Round(expectedAmount, 2) != expectedAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedAmount));
        }
    }

    private static bool HasStringValue(
        JsonElement root,
        string name,
        string expectedValue)
    {
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            string.Equals(
                property.GetString(),
                expectedValue,
                StringComparison.Ordinal);
    }

    private static bool HasAmount(
        JsonElement root,
        string name,
        decimal expectedAmount)
    {
        return root.TryGetProperty(name, out var property) &&
            TryGetDecimalValue(property, out var amount) &&
            amount == expectedAmount;
    }

    private static bool HasStringOrNumberValue(
        JsonElement root,
        string name,
        string expectedValue)
    {
        return root.TryGetProperty(name, out var property) &&
            GetPropertyText(property) is { Length: > 0 } actualValue &&
            string.Equals(
                actualValue,
                expectedValue,
                StringComparison.Ordinal);
    }

    private static bool IsPositiveIdentifier(JsonElement property)
    {
        return GetPropertyText(property) is { } value &&
            IsPositiveIdentifier(value);
    }

    private static bool IsPositiveIdentifier(string? value)
    {
        return !string.IsNullOrEmpty(value) &&
            value.All(char.IsAsciiDigit) &&
            long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var identifier) &&
            identifier > 0 &&
            identifier.ToString(CultureInfo.InvariantCulture) == value;
    }

    private static string? GetPropertyText(JsonElement property)
    {
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : null,
            _ => null
        };
    }

    private static string GetIdentifierText(JsonElement property)
    {
        return GetPropertyText(property)
            ?? throw new InvalidOperationException(
                "Reference identifier is required.");
    }

    private static bool TryGetDecimalValue(
        JsonElement property,
        out decimal value)
    {
        value = default;

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();

            return !string.IsNullOrWhiteSpace(text) &&
                decimal.TryParse(
                    text,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetDecimal(out value);
        }

        return false;
    }

    private static bool IsUnambiguousObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }
}