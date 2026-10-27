namespace VirtualEmployee.Application.Payments.CreatePaymentAttempt;

public enum CreatePaymentAttemptError
{
    None = 0,
    InvalidRequest = 1,
    AppointmentNotFound = 2,
    InvalidAppointmentState = 3,
    ReservationExpired = 4,
    PaymentAlreadyConfirmed = 5,
    IdempotencyConflict = 6,
    ConcurrencyConflict = 7,
    ProviderError = 8
}