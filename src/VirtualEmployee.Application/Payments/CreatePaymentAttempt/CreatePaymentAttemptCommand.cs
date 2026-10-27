using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Application.Payments.CreatePaymentAttempt;

public sealed record CreatePaymentAttemptCommand(
    Guid AppointmentId,
    PaymentMethod PaymentMethod,
    string IdempotencyKey);