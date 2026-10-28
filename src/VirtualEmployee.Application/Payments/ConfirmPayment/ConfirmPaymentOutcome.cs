namespace VirtualEmployee.Application.Payments.ConfirmPayment;

public enum ConfirmPaymentOutcome
{
    Confirmed = 1,
    ReservationExpired = 2,
    AppointmentNotConfirmable = 3,
    AdditionalPayment = 4,
    NotFound = 5,
    InvalidNotification = 6,
    ConcurrencyConflict = 7,
    IdempotencyConflict = 8
}