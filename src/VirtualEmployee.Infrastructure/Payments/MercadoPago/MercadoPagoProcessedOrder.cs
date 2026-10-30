namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed record MercadoPagoProcessedOrder(
    string OrderId,
    string PaymentId,
    Guid PaymentAttemptId,
    long CollectorId,
    long ApplicationId,
    decimal Amount,
    string Currency,
    string PaymentType);