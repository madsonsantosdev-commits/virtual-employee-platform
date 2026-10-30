namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed record MercadoPagoApprovedPayment(
    string PaymentId,
    long CollectorId,
    Guid PaymentAttemptId,
    decimal Amount,
    string Currency,
    DateTimeOffset ApprovedAt);