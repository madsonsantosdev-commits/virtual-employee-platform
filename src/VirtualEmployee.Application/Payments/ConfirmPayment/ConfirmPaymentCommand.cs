namespace VirtualEmployee.Application.Payments.ConfirmPayment;

public sealed record ConfirmPaymentCommand(
    Guid PaymentAttemptId,
    string Provider,
    string ProviderOrderId,
    string ProviderPaymentId,
    decimal Amount,
    string Currency,
    DateTimeOffset ApprovedAt);