using System.Globalization;
using System.Text.Json;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoOrderRequestFactoryTests
{
    [Fact]
    public void CreateJson_ShouldUsePersistedAmountAndAttemptReference()
    {
        var request = CreateRequest();

        var json = MercadoPagoOrderRequestFactory.CreateJson(request);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("online", root.GetProperty("type").GetString());
        Assert.Equal(
            "manual",
            root.GetProperty("processing_mode").GetString());
        Assert.Equal(
            "65.00",
            root.GetProperty("total_amount").GetString());
        Assert.Equal(
            request.PaymentAttemptId.ToString("D"),
            root.GetProperty("external_reference").GetString());
        Assert.Equal(
            "PT10M",
            root.GetProperty("expiration_time").GetString());

        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        var item = items[0];

        Assert.Equal("Agendamento", item.GetProperty("title").GetString());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
        Assert.Equal(
            "65.00",
            item.GetProperty("unit_price").GetString());
    }

    [Fact]
    public void CreateJson_WithBrazilianCulture_ShouldUseDecimalPoint()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture =
                CultureInfo.GetCultureInfo("pt-BR");

            var request = CreateRequest() with
            {
                Amount = 1234.56m
            };

            var json = MercadoPagoOrderRequestFactory.CreateJson(request);

            using var document = JsonDocument.Parse(json);

            Assert.Equal(
                "1234.56",
                document.RootElement
                    .GetProperty("total_amount")
                    .GetString());

            Assert.Equal(
                "1234.56",
                document.RootElement
                    .GetProperty("items")[0]
                    .GetProperty("unit_price")
                    .GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void CreateJson_AfterIntentRoundTrip_ShouldPreserveBody()
    {
        var request = CreateRequest();

        var firstBody =
            MercadoPagoOrderRequestFactory.CreateJson(request);

        var storedIntent = JsonSerializer.Serialize(request);

        var recoveredRequest =
            JsonSerializer.Deserialize<CreateGatewayPaymentRequest>(
                storedIntent);

        Assert.NotNull(recoveredRequest);
        Assert.Equal(request.PreparedAt, recoveredRequest.PreparedAt);

        var recoveredBody =
            MercadoPagoOrderRequestFactory.CreateJson(
                recoveredRequest);

        Assert.Equal(firstBody, recoveredBody);
    }

    [Fact]
    public void CreateJson_WithEquivalentOffsets_ShouldPreserveBody()
    {
        var request = CreateRequest();

        var equivalentRequest = request with
        {
            PreparedAt = request.PreparedAt!.Value.ToOffset(
                TimeSpan.FromHours(-3)),
            ExpiresAt = request.ExpiresAt.ToOffset(
                TimeSpan.FromHours(-3))
        };

        Assert.Equal(
            MercadoPagoOrderRequestFactory.CreateJson(request),
            MercadoPagoOrderRequestFactory.CreateJson(
                equivalentRequest));
    }

    [Fact]
    public void CreateJson_ShouldDiscardFractionalSecondsWithoutRoundingUp()
    {
        var request = CreateRequest();

        request = request with
        {
            ExpiresAt = request.PreparedAt!.Value
                .AddSeconds(90)
                .AddMilliseconds(999)
        };

        var json = MercadoPagoOrderRequestFactory.CreateJson(request);

        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            "PT1M30S",
            document.RootElement
                .GetProperty("expiration_time")
                .GetString());
    }

    [Fact]
    public void CreateJson_WithoutPreparationTimestamp_ShouldReject()
    {
        var request = CreateRequest() with
        {
            PreparedAt = null
        };

        Assert.Throws<InvalidOperationException>(() =>
            MercadoPagoOrderRequestFactory.CreateJson(request));
    }

    [Theory]
    [InlineData(PaymentMethod.CreditCard, "credit_card", "debit_card")]
    [InlineData(PaymentMethod.DebitCard, "debit_card", "credit_card")]
    public void CreateJson_ShouldExcludeOtherPaymentTypes(
        PaymentMethod method,
        string allowedType,
        string otherCardType)
    {
        var request = CreateRequest() with
        {
            PaymentMethod = method
        };

        var json = MercadoPagoOrderRequestFactory.CreateJson(request);

        using var document = JsonDocument.Parse(json);

        var excludedTypes = document.RootElement
            .GetProperty("config")
            .GetProperty("payment_method")
            .GetProperty("not_allowed_types")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();

        Assert.DoesNotContain(allowedType, excludedTypes);
        Assert.Contains(otherCardType, excludedTypes);
        Assert.Contains("prepaid_card", excludedTypes);
        Assert.Contains("account_money", excludedTypes);
        Assert.Contains("ticket", excludedTypes);
        Assert.Contains("bank_transfer", excludedTypes);
        Assert.Contains("atm", excludedTypes);
        Assert.Contains("digital_currency", excludedTypes);
        Assert.DoesNotContain("wallet", excludedTypes);
        Assert.Equal(7, excludedTypes.Length);

        Assert.False(
            document.RootElement
                .GetProperty("config")
                .GetProperty("payment_method")
                .TryGetProperty("not_allowed_ids", out _));
    }

    [Fact]
    public void CreateJson_WithUnvalidatedPix_ShouldReject()
    {
        var request = CreateRequest() with
        {
            PaymentMethod = PaymentMethod.Pix
        };

        Assert.Throws<InvalidOperationException>(() =>
            MercadoPagoOrderRequestFactory.CreateJson(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    public void CreateJson_WithInvalidValidity_ShouldReject(
        double seconds)
    {
        var request = CreateRequest();

        request = request with
        {
            ExpiresAt = request.PreparedAt!.Value.AddSeconds(seconds)
        };

        Assert.Throws<ArgumentException>(() =>
            MercadoPagoOrderRequestFactory.CreateJson(request));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65.001")]
    [InlineData("10000000000")]
    public void CreateJson_WithInvalidAmount_ShouldReject(
        string amount)
    {
        var request = CreateRequest() with
        {
            Amount = decimal.Parse(
                amount,
                CultureInfo.InvariantCulture)
        };

        Assert.Throws<ArgumentException>(() =>
            MercadoPagoOrderRequestFactory.CreateJson(request));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("business")]
    [InlineData("appointment")]
    [InlineData("payment")]
    [InlineData("attempt")]
    public void CreateJson_WithEmptyIdentifier_ShouldReject(
        string field)
    {
        var original = CreateRequest();

        var request = field switch
        {
            "tenant" => original with
            {
                TenantId = Guid.Empty
            },
            "business" => original with
            {
                BusinessId = Guid.Empty
            },
            "appointment" => original with
            {
                AppointmentId = Guid.Empty
            },
            "payment" => original with
            {
                PaymentId = Guid.Empty
            },
            "attempt" => original with
            {
                PaymentAttemptId = Guid.Empty
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        Assert.Throws<ArgumentException>(() =>
            MercadoPagoOrderRequestFactory.CreateJson(request));
    }

    [Fact]
    public void CreateJson_WithUnknownPaymentMethod_ShouldReject()
    {
        var request = CreateRequest() with
        {
            PaymentMethod = (PaymentMethod)999
        };

        Assert.Throws<ArgumentException>(() =>
            MercadoPagoOrderRequestFactory.CreateJson(request));
    }

    private static CreateGatewayPaymentRequest CreateRequest()
    {
        var preparedAt = new DateTimeOffset(
            2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        return new CreateGatewayPaymentRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            65m,
            PaymentMethod.CreditCard,
            preparedAt.AddMinutes(10))
        {
            PreparedAt = preparedAt
        };
    }
}