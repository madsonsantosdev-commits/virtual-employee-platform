using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoWebhookSignatureValidatorTests
{
    #region Facts

    [Fact]
    public void Validate_WithKnownSignature_ShouldAccept()
    {
        var valid = MercadoPagoWebhookSignatureValidator.Validate(
            Secret,
            Signature,
            RequestId,
            DataId);

        Assert.True(valid);
    }

    [Fact]
    public void Validate_WithReorderedHeaderParts_ShouldAccept()
    {
        var signature = $"v1={Hash}, ts={Timestamp}";

        var valid = MercadoPagoWebhookSignatureValidator.Validate(
            Secret,
            signature,
            RequestId,
            DataId);

        Assert.True(valid);
    }

    [Fact]
    public void Validate_WithUppercaseHexHash_ShouldAccept()
    {
        var signature =
            $"ts={Timestamp},v1={Hash.ToUpperInvariant()}";

        var valid = MercadoPagoWebhookSignatureValidator.Validate(
            Secret,
            signature,
            RequestId,
            DataId);

        Assert.True(valid);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("secret")]
    [InlineData("request-id")]
    [InlineData("data-id")]
    [InlineData("timestamp")]
    public void Validate_WithTamperedInput_ShouldReject(
        string field)
    {
        var secret = field == "secret"
            ? "different-secret"
            : Secret;

        var requestId = field == "request-id"
            ? "99999999-2222-4333-8444-555555555555"
            : RequestId;

        var dataId = field == "data-id"
            ? "ORDTST999"
            : DataId;

        var signature = field == "timestamp"
            ? $"ts=1793217601,v1={Hash}"
            : Signature;

        var valid = MercadoPagoWebhookSignatureValidator.Validate(
            secret,
            signature,
            requestId,
            dataId);

        Assert.False(valid);
    }

    [Theory]
    [InlineData("missing-timestamp")]
    [InlineData("missing-hash")]
    [InlineData("duplicate-timestamp")]
    [InlineData("duplicate-hash")]
    [InlineData("unknown-field")]
    [InlineData("malformed-part")]
    [InlineData("timestamp-letters")]
    [InlineData("timestamp-negative")]
    [InlineData("timestamp-too-long")]
    [InlineData("hash-invalid-hex")]
    [InlineData("hash-too-short")]
    public void Validate_WithMalformedSignature_ShouldReject(
        string scenario)
    {
        var signature = scenario switch
        {
            "missing-timestamp" =>
                $"v1={Hash}",

            "missing-hash" =>
                $"ts={Timestamp}",

            "duplicate-timestamp" =>
                $"ts={Timestamp},ts={Timestamp},v1={Hash}",

            "duplicate-hash" =>
                $"ts={Timestamp},v1={Hash},v1={Hash}",

            "unknown-field" =>
                $"{Signature},extra=value",

            "malformed-part" =>
                $"{Signature},invalid",

            "timestamp-letters" =>
                $"ts=invalid,v1={Hash}",

            "timestamp-negative" =>
                $"ts=-1,v1={Hash}",

            "timestamp-too-long" =>
                $"ts={new string('1', 21)},v1={Hash}",

            "hash-invalid-hex" =>
                $"ts={Timestamp},v1={new string('z', 64)}",

            "hash-too-short" =>
                $"ts={Timestamp},v1=abcd",

            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario))
        };

        var valid = MercadoPagoWebhookSignatureValidator.Validate(
            Secret,
            signature,
            RequestId,
            DataId);

        Assert.False(valid);
    }

    [Theory]
    [InlineData("request-id", null)]
    [InlineData("request-id", "")]
    [InlineData("request-id", "invalid;id")]
    [InlineData("request-id", "invalid\nid")]
    [InlineData("data-id", null)]
    [InlineData("data-id", "")]
    [InlineData("data-id", "invalid;id")]
    [InlineData("data-id", "invalid\nid")]
    public void Validate_WithInvalidIdentifier_ShouldReject(
        string field,
        string? value)
    {
        var requestId = field == "request-id"
            ? value
            : RequestId;

        var dataId = field == "data-id"
            ? value
            : DataId;

        var valid = MercadoPagoWebhookSignatureValidator.Validate(
            Secret,
            Signature,
            requestId,
            dataId);

        Assert.False(valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_WithoutConfiguredSecret_ShouldThrow(
        string? secret)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            MercadoPagoWebhookSignatureValidator.Validate(
                secret!,
                Signature,
                RequestId,
                DataId));
    }

    #endregion

    #region Helpers

    private const string Secret = "webhook-test-secret";

    private const string RequestId =
        "11111111-2222-4333-8444-555555555555";

    private const string DataId = "ORDTST123";

    private const string Timestamp = "1793217600";

    // HMAC-SHA256 calculado separadamente para o manifest:
    // id:ordtst123;request-id:11111111-2222-4333-8444-555555555555;ts:1793217600;
    private const string Hash =
        "929968e166ccaa2e6a77cadf2156dde027d98157688c578388e80737dc37c9c6";

    private const string Signature =
        "ts=" + Timestamp + ",v1=" + Hash;

    #endregion
}