using System.Globalization;
using System.Text.Json;
using System.Xml;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public static class MercadoPagoOrderRequestFactory
{
        private static readonly string[] PaymentTypes =
    [
        "account_money",
        "atm",
        "bank_transfer",
        "credit_card",
        "debit_card",
        "digital_currency",
        "prepaid_card",
        "ticket"
    ];

    public static string CreateJson(
        CreateGatewayPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId == Guid.Empty ||
            request.BusinessId == Guid.Empty ||
            request.AppointmentId == Guid.Empty ||
            request.PaymentId == Guid.Empty ||
            request.PaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment request identifiers must not be empty.",
                nameof(request));
        }

        if (!Enum.IsDefined(request.PaymentMethod))
        {
            throw new ArgumentException(
                "Payment method is invalid.",
                nameof(request));
        }

        var allowedType = request.PaymentMethod switch
        {
            PaymentMethod.CreditCard => "credit_card",
            PaymentMethod.DebitCard => "debit_card",

            PaymentMethod.Pix => throw new InvalidOperationException(
                "Pix availability has not been validated " +
                "for this Mercado Pago integration."),

            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                "Payment method is not supported.")
        };

        if (request.Amount <= 0 ||
            request.Amount > 9_999_999_999.99m ||
            decimal.Round(request.Amount, 2) != request.Amount)
        {
            throw new ArgumentException(
                "Payment amount is invalid.",
                nameof(request));
        }

        if (request.PreparedAt is not { } preparedAt)
        {
            throw new InvalidOperationException(
                "Payment preparation timestamp is required " +
                "to create a Mercado Pago order.");
        }

        var validity = request.ExpiresAt.ToUniversalTime() -
            preparedAt.ToUniversalTime();

        if (validity <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Payment expiration must follow preparation.",
                nameof(request));
        }

        var wholeSeconds = validity.Ticks /
            TimeSpan.TicksPerSecond;

        if (wholeSeconds == 0)
        {
            throw new ArgumentException(
                "Payment validity must be at least one second.",
                nameof(request));
        }

        var expirationTime = XmlConvert.ToString(
            TimeSpan.FromTicks(
                wholeSeconds * TimeSpan.TicksPerSecond));

        var amount = request.Amount.ToString(
            "0.00",
            CultureInfo.InvariantCulture);

        var excludedTypes = PaymentTypes
            .Where(type => !string.Equals(
                type,
                allowedType,
                StringComparison.Ordinal))
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            type = "online",
            processing_mode = "manual",
            total_amount = amount,
            external_reference =
                request.PaymentAttemptId.ToString("D"),
            expiration_time = expirationTime,
            items = new[]
            {
                new
                {
                    title = "Agendamento",
                    quantity = 1,
                    unit_price = amount
                }
            },
            config = new
            {
                payment_method = new
                {
                    not_allowed_types = excludedTypes
                }
            }
        });
    }
}