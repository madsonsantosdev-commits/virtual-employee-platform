using VirtualEmployee.Infrastructure.Payments.MercadoPago;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoExpiredOrderReaderTests
{
    private static readonly Guid AttemptId = Guid.Parse("3bd99a6a-424c-43bd-bca7-4fa03d8ed571");
    private const string Json = """
        {"id":"ORDTST123","type":"online","status":"canceled","status_detail":"expired",
         "currency":"BRL","user_id":"123","external_reference":"3bd99a6a-424c-43bd-bca7-4fa03d8ed571",
         "total_amount":"65.00","integration_data":{"application_id":"456"}}
        """;

    [Fact]
    public void IsExpired_WithNoTransactions_ShouldAcceptVerifiedIdentity() =>
        Assert.True(Read(Json));

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    public void IsExpired_MalformedOrWrongShape_ShouldReject(string json) =>
        Assert.False(Read(json));

    [Theory]
    [InlineData("\"status\":\"processed\",")]
    [InlineData("\"total_amount\":\"65.00\",")]
    [InlineData("\"total_paid_amount\":\"1\",")]
    [InlineData("\"transactions\":{\"payments\":[{}]},")]
    [InlineData("\"transactions\":null,")]
    public void IsExpired_DuplicatesOrFinancialUncertainty_ShouldReject(string extra) =>
        Assert.False(Read("{" + extra + Json.TrimStart()[1..]));

    [Fact]
    public void IsExpired_DuplicateNestedAccountId_ShouldReject() =>
        Assert.False(Read(Json.Replace("\"application_id\":\"456\"",
            "\"application_id\":\"1\",\"application_id\":\"456\"")));

    [Fact]
    public void IsExpired_WithExplicitZeroAndEmptyPayments_ShouldAccept() =>
        Assert.True(Read("{\"total_paid_amount\":0,\"transactions\":{\"payments\":[]}," + Json.TrimStart()[1..]));

    private static bool Read(string json) =>
        MercadoPagoExpiredOrderReader.IsExpired(json, "ORDTST123", 123, 456, AttemptId, 65m);
}
