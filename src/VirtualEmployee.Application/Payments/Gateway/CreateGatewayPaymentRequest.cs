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
    // A mesma tentativa mantém a mesma chave em todas as chamadas.
    public string IdempotencyKey =>
        $"payment-attempt:{TenantId:N}:{PaymentAttemptId:N}";
}