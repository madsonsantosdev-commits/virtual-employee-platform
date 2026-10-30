using System.Security.Cryptography;
using System.Text;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoWebhookSignatureCasingTests
{
    private const string Secret = "webhook-test-secret";
    private const string RequestId =
        "11111111-2222-4333-8444-555555555555";
    private const string Timestamp = "1793217600";

    #region Facts

    #endregion

    #region Theories

    [Theory]
    [InlineData("ORDTST123", "ORDTST123")]
    [InlineData("ORDTST123", "ordtst123")]
    [InlineData("ordtst123", "ordtst123")]
    [InlineData("OrdTst123", "OrdTst123")]
    public void Validate_WithSupportedSignedRepresentation_ShouldAccept(
        string receivedId,
        string signedId)
    {
        Assert.True(MercadoPagoWebhookSignatureValidator.Validate(
            Secret,
            Sign(Secret, signedId, RequestId),
            RequestId,
            receivedId));
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("data")]
    [InlineData("request")]
    [InlineData("hash")]
    public void Validate_WithTamperedInput_ShouldReject(string changedField)
    {
        var signature = Sign(Secret, "ORDTST123", RequestId);

        if (changedField == "hash")
        {
            signature = $"ts={Timestamp},v1={new string('0', 64)}";
        }

        Assert.False(MercadoPagoWebhookSignatureValidator.Validate(
            changedField == "secret" ? "another-secret" : Secret,
            signature,
            changedField == "request" ? "another-request" : RequestId,
            changedField == "data" ? "ORDTST999" : "ORDTST123"));
    }

    #endregion

    #region Helpers

    private static string Sign(
        string secret,
        string dataId,
        string requestId)
    {
        var manifest =
            $"id:{dataId};request-id:{requestId};ts:{Timestamp};";

        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(manifest));

        return $"ts={Timestamp},v1={Convert.ToHexString(hash)}";
    }

    #endregion
}