using System.Globalization;
using System.Text.Json;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public static class MercadoPagoPaymentResponseReader
{
    public static MercadoPagoApprovedPayment? Read(
        string? responseJson,
        string expectedPaymentId,
        long expectedCollectorId,
        Guid expectedPaymentAttemptId,
        decimal expectedAmount)
    {
        ValidateExpectedValues(
            expectedPaymentId,
            expectedCollectorId,
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

            if (root.ValueKind != JsonValueKind.Object ||
                HasDuplicateProperties(root))
            {
                return null;
            }

            if (!TryGetPositiveInt64(root, "id", out var paymentId) ||
                paymentId.ToString(CultureInfo.InvariantCulture) !=
                    expectedPaymentId)
            {
                return null;
            }

            if (!TryGetPositiveInt64(
                    root,
                    "collector_id",
                    out var collectorId) ||
                collectorId != expectedCollectorId)
            {
                return null;
            }

            if (!HasStringValue(root, "status", "approved") ||
                !HasStringValue(root, "status_detail", "accredited") ||
                !HasStringValue(root, "currency_id", "BRL"))
            {
                return null;
            }

            if (!TryGetDecimal(
                    root,
                    "transaction_amount",
                    out var amount) ||
                amount != expectedAmount)
            {
                return null;
            }

            if (!TryGetDecimal(
                    root,
                    "transaction_amount_refunded",
                    out var refundedAmount) ||
                refundedAmount != 0m)
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
                    out var paymentAttemptId) ||
                paymentAttemptId != expectedPaymentAttemptId)
            {
                return null;
            }

            if (!root.TryGetProperty(
                    "date_approved",
                    out var approvalDate) ||
                approvalDate.ValueKind != JsonValueKind.String ||
                !HasExplicitOffset(approvalDate.GetString()) ||
                !approvalDate.TryGetDateTimeOffset(out var approvedAt) ||
                approvedAt == default)
            {
                return null;
            }

            return new MercadoPagoApprovedPayment(
                expectedPaymentId,
                collectorId,
                paymentAttemptId,
                amount,
                "BRL",
                approvedAt.ToUniversalTime());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateExpectedValues(
        string expectedPaymentId,
        long expectedCollectorId,
        Guid expectedPaymentAttemptId,
        decimal expectedAmount)
    {
        if (string.IsNullOrWhiteSpace(expectedPaymentId) ||
            expectedPaymentId.Any(character =>
                !char.IsAsciiDigit(character)) ||
            !long.TryParse(
                expectedPaymentId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var paymentId) ||
            paymentId <= 0 ||
            paymentId.ToString(CultureInfo.InvariantCulture) !=
                expectedPaymentId)
        {
            throw new ArgumentException(
                "Expected payment ID is invalid.",
                nameof(expectedPaymentId));
        }

        if (expectedCollectorId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedCollectorId));
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

    private static bool TryGetPositiveInt64(
        JsonElement root,
        string name,
        out long value)
    {
        value = default;

        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out value) &&
            value > 0;
    }

    private static bool TryGetDecimal(
        JsonElement root,
        string name,
        out decimal value)
    {
        value = default;

        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDecimal(out value);
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

    private static bool HasExplicitOffset(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.EndsWith('Z'))
        {
            return true;
        }

        return value.Length >= 6 &&
            (value[^6] == '+' || value[^6] == '-') &&
            value[^3] == ':';
    }

    private static bool HasDuplicateProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return true;
            }
        }

        return false;
    }
}