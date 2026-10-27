namespace VirtualEmployee.Application.Payments.CreatePaymentAttempt;

public sealed record CreatePaymentAttemptResponse(
    Guid PaymentId,
    Guid PaymentAttemptId,
    string PaymentMethod,
    string Status,
    decimal Amount,
    string? CheckoutUrl,
    DateTimeOffset ExpiresAt);