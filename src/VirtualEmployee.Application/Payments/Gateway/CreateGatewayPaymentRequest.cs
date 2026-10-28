using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Application.Payments.Gateway;

public sealed record CreateGatewayPaymentRequest(
    Guid TenantId,
    Guid BusinessId,
    Guid AppointmentId,
    Guid PaymentId,
    Guid PaymentAttemptId,
    decimal Amount,
    PaymentMethod PaymentMethod,
    DateTimeOffset ExpiresAt)
{
    public DateTimeOffset? PreparedAt { get; init; }

    public string IdempotencyKey =>
        $"payment-attempt:{TenantId:N}:{PaymentAttemptId:N}";
}