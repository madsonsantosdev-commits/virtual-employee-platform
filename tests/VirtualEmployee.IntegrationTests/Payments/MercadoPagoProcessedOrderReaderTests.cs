using System.Globalization;
using System.Text.Json.Nodes;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoProcessedOrderReaderTests
{
    private const string OrderId = "ORDTST123";
    private const long CollectorId = 987654321;
    private const long ApplicationId = 123456789;
    private const decimal Amount = 65.00m;
    private static readonly Guid AttemptId =
        Guid.Parse("10000000-0000-4000-8000-000000000001");

    #region Facts

    [Fact]
    public void Read_WithValidResponse_ShouldReturnExpectedValues()
    {
        var result = Read(CreateValidObject().ToJsonString());

        Assert.NotNull(result);
        Assert.Equal(OrderId, result.OrderId);
        Assert.Equal("123456", result.PaymentId);
        Assert.Equal(AttemptId, result.PaymentAttemptId);
        Assert.Equal(CollectorId, result.CollectorId);
        Assert.Equal(ApplicationId, result.ApplicationId);
        Assert.Equal(Amount, result.Amount);
        Assert.Equal("BRL", result.Currency);
        Assert.Equal("credit_card", result.PaymentType);
    }

    [Fact]
    public void Read_WithMismatchedAmount_ShouldReturnNull()
    {
        var root = CreateValidObject();
        root["total_amount"] = "66.00";
        Assert.Null(Read(root.ToJsonString()));
    }

    [Fact]
    public void Read_ShouldUseReferenceIdInsteadOfTransactionId()
    {
        var root = CreateValidObject();
        GetPayment(root)["id"] = "PAY-OTHER";
        var result = Read(root.ToJsonString());
        Assert.NotNull(result);
        Assert.Equal("123456", result.PaymentId);
    }

    [Fact]
    public void Read_WithMultiplePayments_ShouldReturnNull()
    {
        var root = CreateValidObject();
        var payments = root["transactions"]!["payments"]!.AsArray();
        payments.Add(payments[0]!.DeepClone());
        Assert.Null(Read(root.ToJsonString()));
    }

    [Fact]
    public void Read_WithEmptyExpectedAttempt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            MercadoPagoProcessedOrderReader.Read(
                CreateValidObject().ToJsonString(), OrderId,
                CollectorId, ApplicationId, Guid.Empty, Amount));
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("user_id", 111111111)]
    [InlineData("application_id", 999999999)]
    [InlineData("status", "pending")]
    public void Read_WithUnexpectedPayload_ShouldReturnNull(
        string propertyName, object value)
    {
        var root = CreateValidObject();
        var target = propertyName == "application_id"
            ? root["integration_data"]!.AsObject()
            : root;
        target[propertyName] = JsonSerializerNode(value);
        Assert.Null(Read(root.ToJsonString()));
    }

    [Theory]
    [InlineData("credit_card")]
    [InlineData("debit_card")]
    public void Read_WithSupportedPaymentType_ShouldReturnResult(string type)
    {
        var root = CreateValidObject();
        GetPayment(root)["payment_method"]!["type"] = type;
        Assert.Equal(type, Read(root.ToJsonString())?.PaymentType);
    }

    [Theory]
    [InlineData("user_id", "987654321")]
    [InlineData("integration_data.application_id", "123456789")]
    [InlineData("total_amount", "65")]
    [InlineData("total_paid_amount", "65.00")]
    [InlineData("payment.amount", "65")]
    [InlineData("payment.paid_amount", "65.00")]
    [InlineData("payment.reference_id", "123456")]
    public void Read_WithNumericField_ShouldAcceptEquivalentValue(
        string path, string numberJson)
    {
        var root = CreateValidObject();
        Set(root, path, JsonNode.Parse(numberJson));
        Assert.NotNull(Read(root.ToJsonString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("123")]
    public void Read_WithInvalidJson_ShouldReturnNull(string? json)
    {
        Assert.Null(Read(json));
    }

    [Theory]
    [InlineData("id", "\"OTHER\"")]
    [InlineData("type", "\"offline\"")]
    [InlineData("status", "\"created\"")]
    [InlineData("status_detail", "\"pending\"")]
    [InlineData("currency", "\"USD\"")]
    [InlineData("external_reference", "\"invalid\"")]
    [InlineData("user_id", "true")]
    [InlineData("user_id", "987654321.5")]
    [InlineData("integration_data.application_id", "\"OTHER\"")]
    [InlineData("total_amount", "\"64.99\"")]
    [InlineData("total_paid_amount", "\"66\"")]
    [InlineData("total_amount", "\"6,5\"")]
    [InlineData("total_amount", "\"+65\"")]
    [InlineData("total_amount", "\" 65 \"")]
    [InlineData("total_amount", "\"6.5e1\"")]
    [InlineData("payment.status", "\"pending\"")]
    [InlineData("payment.status_detail", "\"pending\"")]
    [InlineData("payment.amount", "\"66\"")]
    [InlineData("payment.paid_amount", "\"64\"")]
    [InlineData("payment.reference_id", "\"0\"")]
    [InlineData("payment.reference_id", "\"00123\"")]
    [InlineData("payment.reference_id", "\"PAY123\"")]
    [InlineData("payment.reference_id", "123.5")]
    [InlineData("payment.reference_id", "\"9223372036854775808\"")]
    [InlineData("payment.payment_method.type", "\"pix\"")]
    [InlineData("payment.payment_method.type", "true")]
    [InlineData("transactions.payments", "[]")]
    [InlineData("transactions.payments", "[null]")]
    [InlineData("transactions.payments", "{}")]
    public void Read_WithInvalidValue_ShouldReturnNull(
        string path, string valueJson)
    {
        var root = CreateValidObject();
        Set(root, path, JsonNode.Parse(valueJson));
        Assert.Null(Read(root.ToJsonString()));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("status")]
    [InlineData("status_detail")]
    [InlineData("currency")]
    [InlineData("user_id")]
    [InlineData("external_reference")]
    [InlineData("total_amount")]
    [InlineData("total_paid_amount")]
    [InlineData("integration_data")]
    [InlineData("integration_data.application_id")]
    [InlineData("transactions")]
    [InlineData("transactions.payments")]
    [InlineData("payment.reference_id")]
    [InlineData("payment.status")]
    [InlineData("payment.status_detail")]
    [InlineData("payment.amount")]
    [InlineData("payment.paid_amount")]
    [InlineData("payment.payment_method")]
    [InlineData("payment.payment_method.type")]
    public void Read_WithMissingOrNullField_ShouldReturnNull(string path)
    {
        var missing = CreateValidObject();
        var (parent, name) = Locate(missing, path);
        Assert.True(parent.Remove(name));
        Assert.Null(Read(missing.ToJsonString()));

        var withNull = CreateValidObject();
        Set(withNull, path, null);
        Assert.Null(Read(withNull.ToJsonString()));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("integration_data")]
    [InlineData("transactions")]
    [InlineData("payment")]
    [InlineData("payment_method")]
    public void Read_WithDuplicateProperty_ShouldReturnNull(string location)
    {
        var root = CreateValidObject();
        var target = location switch
        {
            "root" => root,
            "integration_data" => root["integration_data"]!.AsObject(),
            "transactions" => root["transactions"]!.AsObject(),
            "payment" => GetPayment(root),
            "payment_method" => GetPayment(root)["payment_method"]!.AsObject(),
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };
        var original = target.ToJsonString();
        var duplicate = original.Insert(1, "\"duplicate\":1,\"duplicate\":2,");
        var json = root.ToJsonString().Replace(
            original, duplicate, StringComparison.Ordinal);
        Assert.Null(Read(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../orders")]
    [InlineData("ORD/123")]
    [InlineData("ORD?query=value")]
    public void Read_WithInvalidExpectedOrderId_ShouldThrow(string orderId)
    {
        Assert.Throws<ArgumentException>(() =>
            MercadoPagoProcessedOrderReader.Read(
                CreateValidObject().ToJsonString(), orderId,
                CollectorId, ApplicationId, AttemptId, Amount));
    }

    [Theory]
    [InlineData(0L, 123456789L)]
    [InlineData(-1L, 123456789L)]
    [InlineData(987654321L, 0L)]
    [InlineData(987654321L, -1L)]
    public void Read_WithInvalidExpectedAccount_ShouldThrow(
        long collectorId, long applicationId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MercadoPagoProcessedOrderReader.Read(
                CreateValidObject().ToJsonString(), OrderId,
                collectorId, applicationId, AttemptId, Amount));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65.001")]
    [InlineData("10000000000")]
    public void Read_WithInvalidExpectedAmount_ShouldThrow(string amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MercadoPagoProcessedOrderReader.Read(
                CreateValidObject().ToJsonString(), OrderId,
                CollectorId, ApplicationId, AttemptId,
                decimal.Parse(amount, CultureInfo.InvariantCulture)));
    }

    #endregion

    #region Helpers

    private static MercadoPagoProcessedOrder? Read(string? json) =>
        MercadoPagoProcessedOrderReader.Read(
            json, OrderId, CollectorId, ApplicationId, AttemptId, Amount);

    private static JsonNode? JsonSerializerNode(object value) =>
        System.Text.Json.JsonSerializer.SerializeToNode(value);

    private static JsonObject GetPayment(JsonObject root) =>
        root["transactions"]!["payments"]![0]!.AsObject();

    private static (JsonObject Parent, string Name) Locate(
        JsonObject root, string path)
    {
        var parts = path.Split('.');
        JsonObject current = root;
        var start = 0;
        if (parts[0] == "payment")
        {
            current = GetPayment(root);
            start = 1;
        }
        for (var index = start; index < parts.Length - 1; index++)
        {
            current = current[parts[index]]!.AsObject();
        }
        return (current, parts[^1]);
    }

    private static void Set(JsonObject root, string path, JsonNode? value)
    {
        var (parent, name) = Locate(root, path);
        parent[name] = value;
    }

    private static JsonObject CreateValidObject() => new()
    {
        ["id"] = OrderId,
        ["type"] = "online",
        ["status"] = "processed",
        ["status_detail"] = "accredited",
        ["currency"] = "BRL",
        ["user_id"] = CollectorId.ToString(CultureInfo.InvariantCulture),
        ["external_reference"] = AttemptId.ToString("D"),
        ["integration_data"] = new JsonObject
        {
            ["application_id"] = ApplicationId.ToString(CultureInfo.InvariantCulture)
        },
        ["total_amount"] = "65.00",
        ["total_paid_amount"] = "65.00",
        ["transactions"] = new JsonObject
        {
            ["payments"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "PAY123",
                    ["reference_id"] = "123456",
                    ["status"] = "processed",
                    ["status_detail"] = "accredited",
                    ["amount"] = "65",
                    ["paid_amount"] = "65",
                    ["payment_method"] = new JsonObject
                    {
                        ["type"] = "credit_card"
                    }
                }
            }
        }
    };

    #endregion
}
