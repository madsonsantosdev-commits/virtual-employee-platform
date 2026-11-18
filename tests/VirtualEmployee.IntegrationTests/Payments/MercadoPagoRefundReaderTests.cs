using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoRefundReaderTests
{
    [Theory]
    [InlineData("ORD-123", "refunded", "processed", "65.00", "REF-123")]
    [InlineData("ORD-456", "refunded", "processed", "65.00", null)]
    [InlineData("ORD-123", "partially_refunded", "processed", "65.00", null)]
    [InlineData("ORD-123", "refunded", "processing", "65.00", null)]
    [InlineData("ORD-123", "refunded", "processed", "64.99", null)]
    public void ReadCompleted_RequiresMatchingFullProcessedRefund(
        string orderId, string statusDetail, string refundStatus, string amount, string? expected)
    {
        var json = $$"""
            {
              "id": "{{orderId}}",
              "status_detail": "{{statusDetail}}",
              "transactions": {
                "refunds": [{ "id": "REF-123", "status": "{{refundStatus}}", "amount": "{{amount}}" }]
              }
            }
            """;

        Assert.Equal(expected, MercadoPagoRefundReader.ReadCompleted(json, "ORD-123", 65m));
    }
}
