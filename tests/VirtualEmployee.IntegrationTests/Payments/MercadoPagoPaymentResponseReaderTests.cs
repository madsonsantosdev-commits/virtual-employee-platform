using System.Text.Json.Nodes;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoPaymentResponseReaderTests
{
    private const string PaymentId = "123456789";
    private const long CollectorId = 987654321;
    private const decimal Amount = 65.00m;

    private static readonly Guid AttemptId =
        Guid.Parse("10000000-0000-4000-8000-000000000001");

    #region Facts

    [Fact]
    public void Read_WithApprovedPayment_ShouldReturnExpectedValues()
    {
        var result = Read(CreateValidResponse());

        Assert.NotNull(result);
        Assert.Equal(PaymentId, result.PaymentId);
        Assert.Equal(CollectorId, result.CollectorId);
        Assert.Equal(AttemptId, result.PaymentAttemptId);
        Assert.Equal(Amount, result.Amount);
        Assert.Equal("BRL", result.Currency);
        Assert.Equal(
            new DateTimeOffset(
                2026, 10, 28, 20, 38, 17,
                TimeSpan.Zero),
            result.ApprovedAt);
        Assert.Equal(TimeSpan.Zero, result.ApprovedAt.Offset);
    }

    [Fact]
    public void Read_WithDifferentExpectedPayment_ShouldReturnNull()
    {
        var result = MercadoPagoPaymentResponseReader.Read(
            CreateValidResponse(),
            "111111111",
            CollectorId,
            AttemptId,
            Amount);

        Assert.Null(result);
    }

    [Fact]
    public void Read_WithDifferentExpectedCollector_ShouldReturnNull()
    {
        var result = MercadoPagoPaymentResponseReader.Read(
            CreateValidResponse(),
            PaymentId,
            111111111,
            AttemptId,
            Amount);

        Assert.Null(result);
    }

    [Fact]
    public void Read_WithDifferentExpectedAttempt_ShouldReturnNull()
    {
        var result = MercadoPagoPaymentResponseReader.Read(
            CreateValidResponse(),
            PaymentId,
            CollectorId,
            Guid.NewGuid(),
            Amount);

        Assert.Null(result);
    }

    [Fact]
    public void Read_WithDifferentExpectedAmount_ShouldReturnNull()
    {
        var result = MercadoPagoPaymentResponseReader.Read(
            CreateValidResponse(),
            PaymentId,
            CollectorId,
            AttemptId,
            66.00m);

        Assert.Null(result);
    }

    [Fact]
    public void Read_WithDuplicateProperty_ShouldReturnNull()
    {
        var json = CreateValidResponse();

        json = json.Insert(
            json.LastIndexOf('}'),
            ""","status":"approved" """);

        Assert.Null(Read(json));
    }

    [Fact]
    public void Read_WithEmptyExpectedAttempt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            MercadoPagoPaymentResponseReader.Read(
                CreateValidResponse(),
                PaymentId,
                CollectorId,
                Guid.Empty,
                Amount));
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("2026-10-28T20:38:17Z")]
    [InlineData("2026-10-28T17:38:17-03:00")]
    [InlineData("2026-10-28T22:38:17+02:00")]
    public void Read_WithEquivalentApprovalDates_ShouldNormalizeToUtc(
        string approvedAt)
    {
        var json = ChangeProperty(
            "date_approved",
            JsonValue.Create(approvedAt));

        var result = Read(json);

        Assert.NotNull(result);
        Assert.Equal(
            new DateTimeOffset(
                2026, 10, 28, 20, 38, 17,
                TimeSpan.Zero),
            result.ApprovedAt);
        Assert.Equal(TimeSpan.Zero, result.ApprovedAt.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("\"approved\"")]
    public void Read_WithInvalidJson_ShouldReturnNull(string? json)
    {
        Assert.Null(Read(json));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("collector_id")]
    [InlineData("status")]
    [InlineData("status_detail")]
    [InlineData("currency_id")]
    [InlineData("transaction_amount")]
    [InlineData("transaction_amount_refunded")]
    [InlineData("external_reference")]
    [InlineData("date_approved")]
    public void Read_WithMissingRequiredProperty_ShouldReturnNull(
        string propertyName)
    {
        var root = CreateValidObject();

        Assert.True(root.Remove(propertyName));
        Assert.Null(Read(root.ToJsonString()));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("collector_id")]
    [InlineData("status")]
    [InlineData("status_detail")]
    [InlineData("currency_id")]
    [InlineData("transaction_amount")]
    [InlineData("transaction_amount_refunded")]
    [InlineData("external_reference")]
    [InlineData("date_approved")]
    public void Read_WithNullRequiredProperty_ShouldReturnNull(
        string propertyName)
    {
        Assert.Null(Read(ChangeProperty(propertyName, null)));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("in_process")]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    [InlineData("refunded")]
    [InlineData("charged_back")]
    public void Read_WithUnapprovedStatus_ShouldReturnNull(string status)
    {
        Assert.Null(Read(ChangeProperty(
            "status",
            JsonValue.Create(status))));
    }

    [Theory]
    [InlineData("pending_contingency")]
    [InlineData("partially_refunded")]
    [InlineData("")]
    public void Read_WithUnexpectedStatusDetail_ShouldReturnNull(
        string statusDetail)
    {
        Assert.Null(Read(ChangeProperty(
            "status_detail",
            JsonValue.Create(statusDetail))));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("ARS")]
    [InlineData("brl")]
    public void Read_WithUnexpectedCurrency_ShouldReturnNull(string currency)
    {
        Assert.Null(Read(ChangeProperty(
            "currency_id",
            JsonValue.Create(currency))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(64)]
    [InlineData(66)]
    public void Read_WithUnexpectedAmount_ShouldReturnNull(int amount)
    {
        Assert.Null(Read(ChangeProperty(
            "transaction_amount",
            JsonValue.Create(amount))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65)]
    [InlineData(-1)]
    public void Read_WithNonzeroRefundedAmount_ShouldReturnNull(
        int refundedAmount)
    {
        Assert.Null(Read(ChangeProperty(
            "transaction_amount_refunded",
            JsonValue.Create(refundedAmount))));
    }

    [Theory]
    [InlineData("id", "\"123456789\"")]
    [InlineData("id", "0")]
    [InlineData("id", "-1")]
    [InlineData("id", "123456789.5")]
    [InlineData("collector_id", "\"987654321\"")]
    [InlineData("collector_id", "0")]
    [InlineData("collector_id", "-1")]
    [InlineData("transaction_amount", "\"65.00\"")]
    [InlineData("transaction_amount_refunded", "\"0\"")]
    [InlineData("status", "true")]
    [InlineData("status_detail", "123")]
    [InlineData("currency_id", "[]")]
    [InlineData("external_reference", "{}")]
    [InlineData("date_approved", "123")]
    public void Read_WithInvalidPropertyTypeOrValue_ShouldReturnNull(
        string propertyName,
        string propertyJson)
    {
        Assert.Null(Read(ChangeProperty(
            propertyName,
            JsonNode.Parse(propertyJson))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("10000000000040008000000000000001")]
    [InlineData("20000000-0000-4000-8000-000000000002")]
    public void Read_WithInvalidExternalReference_ShouldReturnNull(
        string externalReference)
    {
        Assert.Null(Read(ChangeProperty(
            "external_reference",
            JsonValue.Create(externalReference))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("2026-10-28")]
    [InlineData("2026-10-28T20:38:17")]
    [InlineData("2026-13-28T20:38:17Z")]
    [InlineData("2026-10-28T20:38:17+25:00")]
    [InlineData("0001-01-01T00:00:00Z")]
    public void Read_WithInvalidApprovalDate_ShouldReturnNull(
        string approvedAt)
    {
        Assert.Null(Read(ChangeProperty(
            "date_approved",
            JsonValue.Create(approvedAt))));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-123")]
    [InlineData("00123")]
    [InlineData("ORDTST123")]
    [InlineData("123/456")]
    [InlineData("9223372036854775808")]
    public void Read_WithInvalidExpectedPaymentId_ShouldThrow(
        string expectedPaymentId)
    {
        Assert.Throws<ArgumentException>(() =>
            MercadoPagoPaymentResponseReader.Read(
                CreateValidResponse(),
                expectedPaymentId,
                CollectorId,
                AttemptId,
                Amount));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Read_WithInvalidExpectedCollector_ShouldThrow(
        long expectedCollectorId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MercadoPagoPaymentResponseReader.Read(
                CreateValidResponse(),
                PaymentId,
                expectedCollectorId,
                AttemptId,
                Amount));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65.001")]
    [InlineData("10000000000")]
    public void Read_WithInvalidExpectedAmount_ShouldThrow(
        string expectedAmount)
    {
        var amount = decimal.Parse(
            expectedAmount,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MercadoPagoPaymentResponseReader.Read(
                CreateValidResponse(),
                PaymentId,
                CollectorId,
                AttemptId,
                amount));
    }

    #endregion

    #region Helpers

    private static MercadoPagoApprovedPayment? Read(string? json)
    {
        return MercadoPagoPaymentResponseReader.Read(
            json,
            PaymentId,
            CollectorId,
            AttemptId,
            Amount);
    }

    private static string CreateValidResponse()
    {
        return CreateValidObject().ToJsonString();
    }

    private static JsonObject CreateValidObject()
    {
        return new JsonObject
        {
            ["id"] = 123456789L,
            ["collector_id"] = CollectorId,
            ["status"] = "approved",
            ["status_detail"] = "accredited",
            ["currency_id"] = "BRL",
            ["transaction_amount"] = Amount,
            ["transaction_amount_refunded"] = 0m,
            ["external_reference"] = AttemptId.ToString("D"),
            ["date_approved"] = "2026-10-28T17:38:17-03:00"
        };
    }

    private static string ChangeProperty(
        string propertyName,
        JsonNode? value)
    {
        var root = CreateValidObject();

        root[propertyName] = value;

        return root.ToJsonString();
    }

    #endregion
}